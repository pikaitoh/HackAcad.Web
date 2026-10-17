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

def field_error(body, name):
    match = re.search(r'id="' + re.escape(name) + r'-error"[^>]*>(.*?)</span>', body, re.S)
    return html.unescape(re.sub('<[^>]+>', '', match.group(1))).strip() if match else ''

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
created_email = "created.o'connor+" + uuid.uuid4().hex[:12] + '@hackacad.local'
temporary_users = [test_email, created_email]

try:
    status, url, body = student.request('/')
    check(status == 200 and 'class="landing-hero"' in body, 'anonymous visitors see the Home page')
    check('>Home</a>' in body and 'aria-label="HackAcad home"' in body,
        'visitor navigation includes Home and the home logo')
    check('/Identity/Account/Login' in student.request('/Admin')[1], 'anonymous users are redirected to login')
    check(student.request('/Identity/Account/Register', {'Role': 'Admin'})[0] == 400, 'POST without CSRF token is rejected')
    invalid = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke', 'Email': test_email,
        'Password': 'weak', 'ConfirmPassword': 'weak'})
    check('Dashboard' not in invalid[1] and 'field-validation-error' in invalid[2], 'server rejects weak registration passwords')
    # A weak password keeps these probes non-mutating even if an email rule regresses.
    for email in ('qw@e', 'user@localhost', 'user@-example.com', 'user@example..com', 'first..last@example.com'):
        result = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke learner', 'Email': email,
            'Password': 'weak', 'ConfirmPassword': 'weak'})
        check(field_error(result[2], 'Email') and field_error(result[2], 'Password')
            and 'validation-summary-errors' not in result[2], 'server rejects malformed email inline: ' + email)
    for email in ('sdf@king.com', 'learner@company.edu.my'):
        result = student.post('/Identity/Account/Register', {'DisplayName': 'Learner', 'Email': email,
            'Password': 'weak', 'ConfirmPassword': 'weak'})
        check(not field_error(result[2], 'Email') and field_error(result[2], 'Password'),
            'complete custom-domain email format is accepted without creating an account: ' + email)
    for name in ('A', '   ', 'A' * 51, '--', '<script>name</script>', 'Line\nBreak'):
        result = student.post('/Identity/Account/Register', {'DisplayName': name, 'Email': test_email,
            'Password': 'weak', 'ConfirmPassword': 'weak'})
        check(field_error(result[2], 'DisplayName'), 'server rejects an invalid display name: ' + repr(name))
    for password in ('abcdefgh1!', 'ABCDEFGH1!', 'Abcdefgh!!', 'Abcdefgh12'):
        result = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke learner', 'Email': test_email,
            'Password': password, 'ConfirmPassword': password})
        check(field_error(result[2], 'Password') and 'validation-summary-errors' not in result[2],
            'password complexity failure stays beside Password: ' + password)
    status, url, body = student.post('/Identity/Account/Register', {'DisplayName': 'Smoke learner', 'Email': test_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Admin'})
    check(status == 200 and 'Dashboard' in url and '>Student</span>' in body, 'registration creates a Student and ignores an injected Admin role')
    for home_path in ('/', '/Home', '/Home/Index'):
        home_status, home_url, home_body = student.request(home_path)
        check(home_status == 200 and '/Identity/Account/Dashboard' in home_url
            and 'class="landing-hero"' not in home_body, 'signed-in Home request redirects to Dashboard: ' + home_path)
    check('>Home</a>' not in home_body and 'aria-label="HackAcad dashboard"' in home_body,
        'signed-in navigation omits Home and the logo targets Dashboard')
    check(student.request('/Admin')[0] == 403, 'Student cannot open admin pages')
    check(student.request('/Admin/Courses')[0] == 403, 'Student cannot moderate courses')
    invalid = student.post('/Identity/Account/Profile', {'DisplayName': ''})
    check('field-validation-error' in invalid[2], 'server rejects an empty profile name')
    check('aria-invalid="true"' in invalid[2] and 'DisplayName-error' in invalid[2], 'server validation is associated with the invalid input')
    profile = student.post('/Identity/Account/Profile', {'DisplayName': 'QA <script>unsafe</script>', 'Role': 'Admin'})
    check(profile[0] == 200 and field_error(profile[2], 'DisplayName') and '&lt;script&gt;' in profile[2] and '<script>unsafe</script>' not in profile[2], 'profile safely encodes rejected markup in a display name')
    check(student.request('/Admin')[0] == 403, 'profile input cannot change role')
    profile = student.post('/Identity/Account/Profile', {'DisplayName': "  陈明 O'Connor-2  "})
    check('has been updated' in profile[2] and "陈明 O'Connor-2" in html.unescape(profile[2]) and "  陈明" not in html.unescape(profile[2]),
        'Unicode names and common punctuation are accepted and trimmed')
    long_name = student.post('/Identity/Account/Profile', {'DisplayName': 'A' * 50})
    check('has been updated' in long_name[2], 'display names accept the 50-character boundary')

    other_session = Client()
    check('Dashboard' in other_session.login(test_email)[1], 'account can sign in in a second session')
    wrong_password = student.post('/Identity/Account/ChangePassword', {'CurrentPassword': 'Incorrect@123!',
        'NewPassword': 'Changed@123!', 'ConfirmPassword': 'Changed@123!'})
    check(field_error(wrong_password[2], 'CurrentPassword') and 'validation-summary-errors' not in wrong_password[2], 'wrong current password appears only beside its field')
    changed = student.post('/Identity/Account/ChangePassword', {'CurrentPassword': PASSWORD,
        'NewPassword': 'Changed@123!', 'ConfirmPassword': 'Changed@123!'})
    check('Profile' in changed[1] and 'password has been changed' in changed[2], 'password change succeeds')
    check('/Login' in other_session.request('/Identity/Account/Dashboard')[1], 'password change revokes the other session')
    fresh = Client()
    check('Unable to sign in' in fresh.login(test_email)[2], 'old password no longer authenticates')
    check('Dashboard' in fresh.login(test_email, 'Changed@123!', 'https://example.invalid/')[1], 'external return URL cannot redirect a successful login')

    admin_status, admin_url, admin_body = admin.login('demo.admin@hackacad.local')
    check(admin_status == 200 and urllib.parse.urlparse(admin_url).path == '/Admin'
        and '<h1>Admin dashboard</h1>' in admin_body, 'Admin signs in directly to the Admin dashboard')
    for entry_path in ('/', '/Home/Index', '/Identity/Account/Dashboard', '/Identity/Account/Login', '/Identity/Account/Register'):
        check(urllib.parse.urlparse(admin.request(entry_path)[1]).path == '/Admin',
            'signed-in Admin entry resolves to the Admin dashboard: ' + entry_path)
    brand = re.search(r'<a[^>]*class="navbar-brand"[^>]*>', admin_body).group(0)
    check('href="/Admin"' in brand and '>Dashboard</a>' in admin_body and '>Administration</a>' not in admin_body,
        'Admin logo and single Dashboard navigation point to administration')
    returned = Client().login('demo.admin@hackacad.local', return_url='/Admin/Users')
    check(urllib.parse.urlparse(returned[1]).path == '/Admin/Users', 'Admin login preserves a valid local return destination')
    external = Client().login('demo.admin@hackacad.local', return_url='https://example.invalid/')
    check(urllib.parse.urlparse(external[1]).path == '/Admin', 'external return URL falls back to the Admin dashboard')
    check(admin.request('/Admin')[0] == 200, 'Admin can open the overview')
    check(admin.request('/Admin/Courses')[0] == 200, 'Admin can open course moderation')
    path = edit_path(admin, test_email)
    invalid_role = admin.post(path, edit_data(test_email, role='SuperAdmin'))
    check('Choose a valid role' in invalid_role[2], 'unknown roles are rejected on the server')
    promoted = admin.post(path, edit_data(test_email, role='Teacher'))
    check('/Admin/Users' in promoted[1], 'Admin can promote a Student to Teacher')
    check('/Login' in fresh.request('/Identity/Account/Dashboard')[1], 'role change revokes existing sessions immediately')
    check('>Teacher</span>' in fresh.login(test_email, 'Changed@123!')[2], 'promoted user receives Teacher claims')
    check('/Identity/Account/Dashboard' in fresh.request('/')[1], 'signed-in Teacher is redirected from Home to Dashboard')
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

    invalid_account = admin.post('/Admin/CreateUser', {'DisplayName': 'A', 'Email': 'qw@e',
        'Password': '12345678', 'ConfirmPassword': 'other', 'Role': 'Teacher'})
    check(all(field_error(invalid_account[2], field) for field in ('DisplayName', 'Email', 'Password', 'ConfirmPassword'))
        and 'validation-summary-errors' not in invalid_account[2], 'Admin creation reports each invalid field without a duplicate summary')
    invalid_edit = admin.post(path, edit_data('qw@e', name='A'))
    check(field_error(invalid_edit[2], 'Email') and field_error(invalid_edit[2], 'DisplayName'),
        'Admin edit uses the same name and email rules')
    created = admin.post('/Admin/CreateUser', {'DisplayName': 'Created teacher', 'Email': created_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Teacher'})
    check('/Admin/Users' in created[1], 'Admin can create a Teacher account with apostrophe/plus email characters')
    check('>Teacher</span>' in Client().login(created_email)[2], 'Admin-created account can authenticate with its assigned role')
    duplicate = admin.post('/Admin/CreateUser', {'DisplayName': 'Duplicate', 'Email': created_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Student'})
    check(field_error(duplicate[2], 'Email') and 'validation-summary-errors' not in duplicate[2], 'duplicate email appears only beside the email field')

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
    for confirmation in ('', 'sdf@', 'wrong@hackacad.local'):
        refused_delete = admin.post(delete_path, {'emailConfirmation': confirmation})
        check(refused_delete[0] == 200 and field_error(refused_delete[2], 'emailConfirmation')
            and 'validation-summary-errors' not in refused_delete[2],
            'deletion confirmation error stays beside the input: ' + repr(confirmation))
    check(admin.request(created_path)[0] == 200, 'invalid deletion confirmations leave the account intact')
    deleted_user = admin.post(delete_path, {'emailConfirmation': '  ' + created_email.upper() + '  '})
    check('/Admin/Users' in deleted_user[1] and admin.request(created_path)[0] == 404, 'Admin deletes an unused account and its role marker')
    temporary_users.remove(created_email)
    check(fresh.request('/Identity/Account/Logout')[0] == 405, 'logout cannot be triggered by a GET request')
    logout_status, logout_url, logout_body = fresh.post('/Identity/Account/Logout', {})
    check(logout_status == 200 and 'class="landing-hero"' in logout_body and '>Home</a>' in logout_body,
        'logout returns the visitor to Home with public navigation')
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
