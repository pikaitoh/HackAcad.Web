"""HTTP integration smoke test for a Development instance with demo accounts.

Run against an isolated test database, not production. Creates temporary users
and deletes them at the end. Standard library only; no browser driver required.
Usage: python tests/auth_smoke.py http://127.0.0.1:5097
"""
import http.cookiejar
import html
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5097').rstrip('/')
PASSWORD = os.environ.get('HACKACAD_DEMO_PASSWORD', 'HackAcad@123!')
checks = 0

def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1
    print('PASS', message)

class Client:
    def __init__(self):
        self.cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies))

    def request(self, path, data=None):
        request = urllib.request.Request(BASE + path,
            data=None if data is None else urllib.parse.urlencode(data).encode())
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read().decode()
        return response.status, response.geturl(), body

    def post(self, path, data, token=True):
        data = dict(data)
        if token:
            status, _, body = self.request('/' if path.endswith('/Logout') else path)
            match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
            if not match:
                raise AssertionError('No CSRF token at ' + path + ' (status ' + str(status) + ')')
            data['__RequestVerificationToken'] = html.unescape(match.group(1))
        return self.request(path, data)

    def login(self, email, password=PASSWORD, return_url=''):
        return self.post('/Identity/Account/Login', {'Email': email, 'Password': password, 'ReturnUrl': return_url})

def edit_path(admin, email):
    body = admin.request('/Admin/Users?search=' + urllib.parse.quote(email))[2]
    match = re.search(r'href="(/Admin/EditUser/[^"]+)"', body)
    if not match:
        raise AssertionError('User not found: ' + email)
    return html.unescape(match.group(1))

def edit_data(email, role='Student', disabled=False, name='Smoke learner'):
    return {'DisplayName': name, 'Email': email, 'Role': role, 'IsDisabled': str(disabled).lower()}

admin, student = Client(), Client()
test_email = 'smoke.' + uuid.uuid4().hex[:12] + '@hackacad.local'
created_email = 'created.' + uuid.uuid4().hex[:12] + '@hackacad.local'
temporary_users = [test_email, created_email]

try:
    check(student.request('/')[0] == 200, 'home page is available')
    check('/Identity/Account/Login' in student.request('/Admin')[1], 'anonymous users are redirected to login')
    check(student.request('/Identity/Account/Register', {'Role': 'Admin'})[0] == 400, 'POST without CSRF token is rejected')
    invalid = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke', 'Email': test_email,
        'Password': 'weak', 'ConfirmPassword': 'weak'})
    check('Dashboard' not in invalid[1] and 'field-validation-error' in invalid[2], 'server rejects weak registration passwords')
    status, url, body = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke learner', 'Email': test_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Admin'})
    check(status == 200 and 'Dashboard' in url and '>Student</span>' in body, 'registration creates a Student and ignores an injected Admin role')
    check(student.request('/Admin')[0] == 403, 'Student cannot open admin pages')
    check(student.request('/Admin/Courses')[0] == 403, 'Student cannot moderate courses')
    invalid = student.post('/Identity/Account/Profile', {'DisplayName': ''})
    check('field-validation-error' in invalid[2], 'server rejects an empty profile name')
    check('aria-invalid="true"' in invalid[2] and 'DisplayName-error' in invalid[2], 'server validation is associated with the invalid input')
    profile = student.post('/Identity/Account/Profile', {'DisplayName': 'QA <script>unsafe</script>', 'Role': 'Admin'})
    check(profile[0] == 200 and '&lt;script&gt;' in profile[2] and '<script>unsafe</script>' not in profile[2], 'profile saves and HTML-encodes user content')
    check(student.request('/Admin')[0] == 403, 'profile input cannot change role')

    other_session = Client()
    check('Dashboard' in other_session.login(test_email)[1], 'account can sign in in a second session')
    wrong_password = student.post('/Identity/Account/ChangePassword', {'CurrentPassword': 'Incorrect@123!',
        'NewPassword': 'Changed@123!', 'ConfirmPassword': 'Changed@123!'})
    check('validation-summary-errors' in wrong_password[2], 'password change requires the current password')
    changed = student.post('/Identity/Account/ChangePassword', {'CurrentPassword': PASSWORD,
        'NewPassword': 'Changed@123!', 'ConfirmPassword': 'Changed@123!'})
    check('Profile' in changed[1] and 'password has been changed' in changed[2], 'password change succeeds')
    check('/Login' in other_session.request('/Identity/Account/Dashboard')[1], 'password change revokes the other session')
    fresh = Client()
    check('Unable to sign in' in fresh.login(test_email)[2], 'old password no longer authenticates')
    check('Dashboard' in fresh.login(test_email, 'Changed@123!', 'https://example.invalid/')[1], 'external return URL cannot redirect a successful login')

    check('Dashboard' in admin.login('demo.admin@hackacad.local')[1], 'development Admin can log in')
    check(admin.request('/Admin')[0] == 200, 'Admin can open the overview')
    check(admin.request('/Admin/Courses')[0] == 200, 'Admin can open course moderation')
    path = edit_path(admin, test_email)
    invalid_role = admin.post(path, edit_data(test_email, role='SuperAdmin'))
    check('Choose a valid role' in invalid_role[2], 'unknown roles are rejected on the server')
    promoted = admin.post(path, edit_data(test_email, role='Teacher'))
    check('/Admin/Users' in promoted[1], 'Admin can promote a Student to Teacher')
    check('/Login' in fresh.request('/Identity/Account/Dashboard')[1], 'role change revokes existing sessions immediately')
    check('>Teacher</span>' in fresh.login(test_email, 'Changed@123!')[2], 'promoted user receives Teacher claims')
    check(fresh.request('/Admin')[0] == 403, 'Teacher cannot open admin pages')

    disabled = admin.post(path, edit_data(test_email, role='Teacher', disabled=True))
    check('/Admin/Users' in disabled[1], 'Admin can disable an account')
    check('/Login' in fresh.request('/Identity/Account/Dashboard')[1], 'disabled account loses its active session immediately')
    check('Unable to sign in' in fresh.login(test_email, 'Changed@123!')[2], 'disabled account cannot log in')
    check('/Admin/Users' in admin.post(path, edit_data(test_email, role='Teacher'))[1], 'Admin can re-enable an account')
    check('Dashboard' in fresh.login(test_email, 'Changed@123!')[1], 're-enabled account can log in')

    own_path = edit_path(admin, 'demo.admin@hackacad.local')
    own_change = admin.post(own_path, edit_data('demo.admin@hackacad.local', role='Student'))
    check('own Admin role' in own_change[2], 'Admin cannot remove their own Admin role')
    own_disable = admin.post(own_path, edit_data('demo.admin@hackacad.local', role='Admin', disabled=True))
    check('disable your own account' in own_disable[2], 'Admin cannot disable their own account')
    own_delete = admin.post(own_path.replace('EditUser', 'DeleteUser'), {'emailConfirmation': 'demo.admin@hackacad.local'})
    check('cannot delete your own account' in own_delete[2], 'Admin cannot delete their own account')

    created = admin.post('/Admin/CreateUser', {'DisplayName': 'Created teacher', 'Email': created_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Teacher'})
    check('/Admin/Users' in created[1], 'Admin can create a Teacher account')
    check('>Teacher</span>' in Client().login(created_email)[2], 'Admin-created account can authenticate with its assigned role')
    duplicate = admin.post('/Admin/CreateUser', {'DisplayName': 'Duplicate', 'Email': created_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Student'})
    check('validation-summary-errors' in duplicate[2], 'duplicate email addresses are rejected')

    locked_email = 'lockout.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    temporary_users.append(locked_email)
    locked = Client()
    locked.post('/Identity/Account/Register', {'DisplayName': 'Lockout check', 'Email': locked_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD})
    locked.post('/Identity/Account/Logout', {})
    for _ in range(5):
        locked.login(locked_email, 'Incorrect@123!')
    check('Unable to sign in' in locked.login(locked_email)[2], 'five failed passwords temporarily lock the account')

    # Optional fixture IDs are created only in the isolated database by the test runner.
    retained = os.environ.get('TEST_COURSE_RETAINED_ID')
    unused = os.environ.get('TEST_COURSE_UNUSED_ID')
    if retained and unused:
        edited = admin.post('/Admin/EditCourse/' + retained,
            {'Title': 'Reviewed course', 'Description': 'Reviewed by Admin', 'Category': 'Security', 'IsPublished': 'true'})
        check('/Admin/Courses' in edited[1] and 'Reviewed course' in edited[2], 'Admin can edit and publish another teacher\'s course')
        retained_result = admin.post('/Admin/DeleteCourse/' + retained, {})
        check('has learning records' in retained_result[2], 'course deletion preserves existing student learning records')
        deleted = admin.post('/Admin/DeleteCourse/' + unused, {})
        check('/Admin/Courses' in deleted[1], 'Admin can delete an unused course')
        owner = edit_path(admin, 'demo.teacher@hackacad.local')
        result = admin.post(owner, edit_data('demo.teacher@hackacad.local', role='Student'))
        check('owns courses' in result[2], 'a course owner cannot be demoted while their courses depend on the Teacher role')
        result = admin.post(owner.replace('EditUser', 'DeleteUser'), {'emailConfirmation': 'demo.teacher@hackacad.local'})
        check('owns courses' in result[2], 'a course owner cannot be deleted while they own courses')

    created_path = edit_path(admin, created_email)
    delete_path = created_path.replace('EditUser', 'DeleteUser')
    refused_delete = admin.post(delete_path, {'emailConfirmation': 'wrong@hackacad.local'})
    check("Enter the account" in refused_delete[2], 'account deletion requires the matching email confirmation')
    deleted_user = admin.post(delete_path, {'emailConfirmation': created_email})
    check('/Admin/Users' in deleted_user[1] and admin.request(created_path)[0] == 404, 'Admin deletes an unused account and its role marker')
    temporary_users.remove(created_email)
    check(fresh.request('/Identity/Account/Logout')[0] == 405, 'logout cannot be triggered by a GET request')
    fresh.post('/Identity/Account/Logout', {})
    check('/Login' in fresh.request('/Identity/Account/Dashboard')[1], 'logout removes access to authenticated pages')
    print(f'All {checks} assertions passed.')
finally:
    # Only this run's random test accounts may be deleted.
    admin.login('demo.admin@hackacad.local')
    for email in temporary_users:
        try:
            path = edit_path(admin, email).replace('EditUser', 'DeleteUser')
            result = admin.post(path, {'emailConfirmation': email})
            if '/Admin/Users' not in result[1]:
                print('Cleanup failed for', email, file=sys.stderr)
        except Exception as error:
            print('Cleanup:', str(error), file=sys.stderr)
