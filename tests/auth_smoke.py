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
import json
import struct
import zlib
from email import policy
from email.parser import BytesParser
from email.utils import getaddresses
from pathlib import Path

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5097').rstrip('/')
PASSWORD = os.environ.get('HACKACAD_DEMO_PASSWORD', 'HackAcad@123!')
checks = 0
MAIL_DIR = Path(os.environ['HACKACAD_TEST_MAIL_DIR'])
if not MAIL_DIR.is_dir():
    raise RuntimeError('Configure Development mail capture before running these tests.')
SMS_DIR = Path(os.environ['HACKACAD_TEST_SMS_DIR'])
if not SMS_DIR.is_dir():
    raise RuntimeError('Configure Development SMS capture before running these tests; never send paid SMS from smoke tests.')

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
            source = '/' if path.endswith('/Logout') else path
            if path.rsplit('/', 1)[-1] in ('SendPhoneCode', 'VerifyPhone', 'RemovePhone', 'CancelPhoneVerification'):
                source = '/Identity/Account/Settings'
            if path.rsplit('/', 1)[-1] in ('UploadAvatar', 'RemoveAvatar'):
                source = '/Identity/Account/Profile'
            status, _, body = self.request(source)
            match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
            if not match:
                raise AssertionError('No CSRF token at ' + path + ' (status ' + str(status) + ')')
            data['__RequestVerificationToken'] = html.unescape(match.group(1))
        return self.request(path, data)

    def login(self, email, password=PASSWORD, return_url=''):
        return self.post('/Identity/Account/Login', {'Email': email, 'Password': password, 'ReturnUrl': return_url})

    def binary(self, path):
        try:
            response = self.opener.open(BASE + path, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.headers, response.read()

    def upload(self, content, filename='test.png', content_type='image/png'):
        body = self.request('/Identity/Account/Profile')[2]
        token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body).group(1))
        boundary = 'HackAcad' + uuid.uuid4().hex
        payload = (f'--{boundary}\r\nContent-Disposition: form-data; name="__RequestVerificationToken"\r\n\r\n{token}\r\n'
            f'--{boundary}\r\nContent-Disposition: form-data; name="avatar"; filename="{filename}"\r\nContent-Type: {content_type}\r\n\r\n').encode()
        payload += content + f'\r\n--{boundary}--\r\n'.encode()
        request = urllib.request.Request(BASE + '/Identity/Account/UploadAvatar', data=payload,
            headers={'Content-Type': 'multipart/form-data; boundary=' + boundary})
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.geturl(), response.read().decode()

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

def mail_links(address, action='ConfirmEmail'):
    links = []
    for file in sorted(MAIL_DIR.glob('*.eml'), key=lambda path: path.stat().st_mtime_ns):
        message = BytesParser(policy=policy.default).parsebytes(file.read_bytes())
        if address.lower() not in [email.lower() for _, email in getaddresses(message.get_all('To', []))]:
            continue
        part = message.get_body(preferencelist=('plain',))
        match = re.search(r'https?://\S+/Identity/Account/' + action + r'\?\S+', part.get_content())
        if match:
            parsed = urllib.parse.urlparse(match.group())
            if parsed.netloc != urllib.parse.urlparse(BASE).netloc:
                raise AssertionError('Mail link must point to the isolated test server')
            links.append(parsed.path + '?' + parsed.query)
    return links

def confirm_mail(client, address):
    links = mail_links(address)
    if not links:
        raise AssertionError('No captured verification email for test fixture')
    return client.request(links[-1])

def sms_codes(number):
    messages = []
    for path in sorted(SMS_DIR.glob('*.json'), key=lambda file: file.stat().st_mtime_ns):
        message = json.loads(path.read_text())
        if message['number'] == number:
            messages.append(message)
    return messages

def test_png(width=32, height=24):
    def chunk(kind, value):
        return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value))
    pixels = (b'\x00' + b'\x33\x79\x6e' * width) * height
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b'')

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
    check(status == 200 and 'VerificationPending' in url and 'Check your email' in body,
        'registration requires email verification instead of signing in')
    check('ConfirmEmail?' not in body and '/Login' in student.request('/Identity/Account/Dashboard')[1],
        'pending account receives no browser verification token and no authenticated access')
    check('Unable to sign in' in student.login(test_email)[2], 'unverified email cannot log in with a correct password')
    links = mail_links(test_email)
    check(len(links) == 1, 'registration sends one captured verification email')
    invalid_link = re.sub(r'code=[^&]+', 'code=not-a-valid-token', links[-1])
    check("This link isn" in student.request(invalid_link)[2], 'tampered verification token is rejected')
    wrong_email_link = re.sub(r'email=[^&]+', 'email=other%40hackacad.local', links[-1])
    check("This link isn" in student.request(wrong_email_link)[2], 'verification token cannot confirm a different email')
    check('Email verified' in confirm_mail(student, test_email)[2], 'email link confirms its intended account')
    check('/Login' in student.request('/Identity/Account/Dashboard')[1], 'verification does not create a logged-in session')
    status, url, body = student.login(test_email)
    check(status == 200 and 'Dashboard' in url and '>Student</span>' in body, 'verified registration creates a Student and ignores an injected Admin role')
    for home_path in ('/', '/Home', '/Home/Index'):
        home_status, home_url, home_body = student.request(home_path)
        check(home_status == 200 and '/Identity/Account/Dashboard' in home_url
            and 'class="landing-hero"' not in home_body, 'signed-in Home request redirects to Dashboard: ' + home_path)
    check('>Home</a>' not in home_body and 'aria-label="HackAcad dashboard"' in home_body,
        'signed-in navigation omits Home and the logo targets Dashboard')
    check(student.request('/Admin')[0] == 403, 'Student cannot open admin pages')
    check(student.request('/Admin/Courses')[0] == 403, 'Student cannot moderate courses')
    check('Open account menu' in body and 'Account &amp; security' in body and 'View profile' in body,
        'signed-in navigation offers an avatar menu with real account destinations')
    check(student.binary('/Identity/Account/Avatar')[0] == 404, 'a new account uses the initials fallback without a missing image')
    fake_image = student.upload(b'<svg><script>alert(1)</script></svg>', 'fake.jpg', 'image/jpeg')
    check(field_error(fake_image[2], 'avatar') and student.binary('/Identity/Account/Avatar')[0] == 404,
        'avatar upload rejects non-image data disguised as JPEG')
    oversized_image = student.upload(b'x' * (2 * 1024 * 1024 + 1))
    check(field_error(oversized_image[2], 'avatar'), 'avatar upload rejects a file larger than two MB')
    large_dimensions = student.upload(test_png(4100, 1))
    check(field_error(large_dimensions[2], 'avatar'), 'avatar upload rejects excessive image dimensions before decoding pixels')
    uploaded = student.upload(test_png())
    check('profile picture has been updated' in uploaded[2], 'valid avatar upload is saved')
    avatar_status, avatar_headers, avatar_bytes = student.binary('/Identity/Account/Avatar')
    check(avatar_status == 200 and avatar_headers.get_content_type() == 'image/jpeg'
        and avatar_bytes.startswith(b'\xff\xd8') and avatar_headers['X-Content-Type-Options'] == 'nosniff',
        'avatar is served as a re-encoded JPEG with nosniff')
    check('/Identity/Account/Avatar' in student.request('/Identity/Account/Profile')[2],
        'the uploaded avatar is used in profile and account navigation')
    check('/Login' in Client().request('/Identity/Account/Avatar')[1], 'anonymous users cannot retrieve a private avatar')
    check('profile picture has been removed' in student.post('/Identity/Account/RemoveAvatar', {})[2]
        and student.binary('/Identity/Account/Avatar')[0] == 404, 'avatar removal restores the initials fallback')

    settings = '/Identity/Account/Settings'
    send_phone = '/Identity/Account/SendPhoneCode'
    verify_phone = '/Identity/Account/VerifyPhone'
    check('Phone number' in student.request(settings)[2] and 'SMS test mode is active' in student.request(settings)[2],
        'phone verification screen explicitly identifies local SMS capture')
    invalid_phone = student.post(send_phone, {'Phone.PhoneNumber': '01112345678'})
    check(field_error(invalid_phone[2], 'Phone_PhoneNumber'), 'phone number requires international country-code format')
    number = '+1555' + str(uuid.uuid4().int % 10_000_000).zfill(7)
    phone_session = Client()
    phone_session.login(test_email)
    requested_phone = student.post(send_phone, {'Phone.PhoneNumber': number})
    messages = sms_codes(number)
    check(len(messages) == 1 and 'Enter your code' in requested_phone[2] and 'Current number' not in requested_phone[2],
        'requesting an OTP does not save an unverified phone number')
    code = messages[-1]['code']
    check(f'value="{code}"' not in requested_phone[2] and f'>{code}<' not in requested_phone[2],
        'OTP is never exposed in the rendered verification page')
    cooldown = student.post(send_phone, {'Phone.PhoneNumber': number})
    check('Wait one minute' in cooldown[2] and len(sms_codes(number)) == 1, 'SMS resend cooldown prevents repeated sends')
    wrong_code = str((int(code) + 1) % 1_000_000).zfill(6)
    incorrect = student.post(verify_phone, {'Confirmation.Code': wrong_code})
    check(field_error(incorrect[2], 'Confirmation_Code') and 'Current number' not in incorrect[2],
        'incorrect OTP cannot save or verify the phone number')
    verified_phone = student.post(verify_phone, {'Confirmation.Code': code, 'PhoneNumber': '+19999999999'})
    verified_body = html.unescape(verified_phone[2])
    check('Your phone number is verified' in verified_body and number in verified_body
        and '+19999999999' not in verified_body, 'verified number comes from server state rather than posted phone data')
    check('/Login' in phone_session.request('/Identity/Account/Dashboard')[1], 'phone verification revokes another existing session')
    replay = student.post(verify_phone, {'Confirmation.Code': code})
    check(field_error(replay[2], 'Confirmation_Code'), 'an approved OTP cannot be replayed')
    same_phone = student.post(send_phone, {'Phone.PhoneNumber': number})
    check('already verified' in same_phone[2] and len(sms_codes(number)) == 1, 'already verified number does not send another OTP')
    check(student.post('/Identity/Account/RemovePhone', {}, token=False)[0] == 400, 'phone removal requires CSRF protection')
    removed_phone = student.post('/Identity/Account/RemovePhone', {})
    check('phone number has been removed' in removed_phone[2] and 'Current number' not in removed_phone[2],
        'phone removal clears the verified contact number')
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
    check('Unable to sign in' in Client().login(created_email)[2], 'Admin-created account also requires email verification')
    check('Email not verified' in admin.request('/Admin/Users?search=' + urllib.parse.quote(created_email))[2],
        'Admin can identify an account awaiting email verification')
    confirm_mail(Client(), created_email)
    check('>Teacher</span>' in Client().login(created_email)[2], 'Admin-created account can authenticate with its assigned role')
    duplicate = admin.post('/Admin/CreateUser', {'DisplayName': 'Duplicate', 'Email': created_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Student'})
    check(field_error(duplicate[2], 'Email') and 'validation-summary-errors' not in duplicate[2], 'duplicate email appears only beside the email field')

    locked_email = 'lockout.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    temporary_users.append(locked_email)
    locked = Client()
    locked.post('/Identity/Account/Register', {'DisplayName': 'Lockout check', 'Email': locked_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD})
    confirm_mail(locked, locked_email)
    for _ in range(5):
        locked.login(locked_email, 'Incorrect@123!')
    check('Unable to sign in' in locked.login(locked_email)[2], 'five failed passwords temporarily lock the account')

    pending_email = 'pending.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    temporary_users.append(pending_email)
    pending = Client()
    pending.post('/Identity/Account/Register', {'DisplayName': 'Pending check', 'Email': pending_email,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD})
    pending_links = mail_links(pending_email)
    resent = pending.post('/Identity/Account/ResendVerification', {'Email': pending_email})
    unknown = Client().post('/Identity/Account/ResendVerification', {'Email': 'unknown.' + uuid.uuid4().hex + '@hackacad.local'})
    confirmed_resend = Client().post('/Identity/Account/ResendVerification', {'Email': test_email})
    check('a link will arrive shortly' in resent[2] and 'a link will arrive shortly' in unknown[2]
        and 'a link will arrive shortly' in confirmed_resend[2], 'resend does not reveal account existence or verification state')
    check(len(mail_links(pending_email)) == len(pending_links), 'immediate resend respects the per-account email cooldown')
    pending_path = edit_path(admin, pending_email)
    admin.post(pending_path, edit_data(pending_email, disabled=True))
    check("This link isn" in pending.request(pending_links[-1])[2], 'a disabled account cannot be verified through an old link')
    count = len(mail_links(pending_email))
    pending.post('/Identity/Account/ResendVerification', {'Email': pending_email})
    check(len(mail_links(pending_email)) == count, 'disabled accounts receive no verification email')

    # Editing an email requires proof of ownership again and invalidates old links/sessions.
    changed_email = 'changed.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    temporary_users.append(changed_email)
    old_link = mail_links(created_email)[-1]
    old_session = Client()
    old_session.login(created_email)
    created_path = edit_path(admin, created_email)
    changed_email_result = admin.post(created_path, edit_data(changed_email, role='Teacher'))
    check('/Admin/Users' in changed_email_result[1], 'Admin can update an email address')
    check('/Login' in old_session.request('/Identity/Account/Dashboard')[1], 'email change revokes the old session')
    check('Unable to sign in' in Client().login(changed_email)[2], 'new email must be verified before login')
    check("This link isn" in Client().request(old_link)[2], 'old email verification link cannot verify a changed address')
    check(len(mail_links(changed_email)) == 1, 'changed address receives its own verification email without the old address cooldown')
    check('Email verified' in confirm_mail(Client(), changed_email)[2], 'new email can be verified with its new link')
    check('>Teacher</span>' in Client().login(changed_email)[2], 'verified new email retains the assigned role')
    temporary_users.remove(created_email)
    created_email = changed_email

    # Self-service email changes keep the working address until a bound link is confirmed.
    change_path = '/Identity/Account/ChangeEmail'
    changer, change_session = Client(), Client()
    changer.login(created_email)
    change_session.login(created_email)
    check('/Login' in Client().request(change_path)[1], 'email-change form requires authentication')
    check(changer.post(change_path, {}, token=False)[0] == 400, 'email-change request requires CSRF protection')
    profile_body = changer.request('/Identity/Account/Profile')[2]
    main = profile_body.split('<main', 1)[1].split('</main>', 1)[0]
    check('>Username</label>' in main and 'Display name' not in main
        and 'Account &amp; security' not in main and 'Change email' in main,
        'Profile uses Username and a change-email button without the duplicate security action')
    invalid_change = changer.post(change_path, {'NewEmail': 'qw@e', 'CurrentPassword': ''})
    check(field_error(invalid_change[2], 'NewEmail') and field_error(invalid_change[2], 'CurrentPassword'),
        'email-change validation stays beside the relevant fields')
    new_address = 'self.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    temporary_users.append(new_address)
    wrong_change = changer.post(change_path, {'NewEmail': new_address, 'CurrentPassword': 'Wrong@123!'})
    check(field_error(wrong_change[2], 'CurrentPassword') and not mail_links(new_address, 'ConfirmEmailChange'),
        'incorrect current password cannot request an email change')
    same_address = changer.post(change_path, {'NewEmail': created_email, 'CurrentPassword': PASSWORD})
    check(field_error(same_address[2], 'NewEmail'), 'email-change request rejects the current address')
    duplicate_address = changer.post(change_path, {'NewEmail': 'demo.admin@hackacad.local', 'CurrentPassword': PASSWORD})
    check(field_error(duplicate_address[2], 'NewEmail'), 'email-change request rejects an unavailable address')
    requested = changer.post(change_path, {'NewEmail': '  ' + new_address + '  ', 'CurrentPassword': PASSWORD,
        'CurrentEmail': 'injected@hackacad.local', 'userId': 'injected'})
    change_links = mail_links(new_address, 'ConfirmEmailChange')
    check('A confirmation link was sent' in requested[2] and len(change_links) == 1
        and 'ConfirmEmailChange?' not in requested[2] and 'value="' + PASSWORD + '"' not in requested[2],
        'email-change sends a captured link to the trimmed address without exposing token or password')
    check(created_email in changer.request('/Identity/Account/Profile')[2]
        and 'Dashboard' in Client().login(created_email)[1], 'pending email change preserves the old login and stored address')
    cooldown_address = 'cooldown.' + uuid.uuid4().hex[:12] + '@hackacad.local'
    blocked = changer.post(change_path, {'NewEmail': cooldown_address, 'CurrentPassword': PASSWORD})
    check("couldn&#x27;t send" in blocked[2] or "couldn't send" in html.unescape(blocked[2]),
        'email-change cooldown blocks repeated requests across destinations')
    check(not mail_links(cooldown_address, 'ConfirmEmailChange'), 'email-change cooldown sends no extra message')
    bad_change_link = re.sub(r'code=[^&]+', 'code=not-a-valid-token', change_links[-1])
    check("This link isn" in Client().request(bad_change_link)[2], 'tampered email-change token is rejected')
    target_change_link = re.sub(r'email=[^&]+', 'email=other%40hackacad.local', change_links[-1])
    check("This link isn" in Client().request(target_change_link)[2], 'email-change token is bound to its target address')
    id_change_link = re.sub(r'userId=[^&]+', 'userId=missing-account', change_links[-1])
    check("This link isn" in Client().request(id_change_link)[2], 'email-change token cannot target a different account')

    # The address can be claimed after the message is sent. A failed transaction must leave the old login usable.
    conflict = admin.post('/Admin/CreateUser', {'DisplayName': 'Conflict fixture', 'Email': new_address,
        'Password': PASSWORD, 'ConfirmPassword': PASSWORD, 'Role': 'Student'})
    check('/Admin/Users' in conflict[1], 'email-change conflict fixture claims the proposed address')
    check("This link isn" in Client().request(change_links[-1])[2]
        and 'Dashboard' in Client().login(created_email)[1], 'a late email conflict cannot partially change the original account')
    conflict_path = edit_path(admin, new_address).replace('EditUser', 'DeleteUser')
    check('/Admin/Users' in admin.post(conflict_path, {'emailConfirmation': new_address})[1], 'late-conflict fixture is removed')
    confirmed_change = changer.request(change_links[-1])
    check('Email changed</h1>' in confirmed_change[2], 'the bound email-change link confirms the new address atomically')
    check('/Login' in changer.request('/Identity/Account/Profile')[1]
        and '/Login' in change_session.request('/Identity/Account/Dashboard')[1], 'email change signs out current and other sessions')
    check('Unable to sign in' in Client().login(created_email)[2], 'old email cannot authenticate after confirmed change')
    check('>Teacher</span>' in Client().login(new_address)[2], 'new email logs in with verification and assigned role preserved')
    check("This link isn" in Client().request(change_links[-1])[2], 'confirmed email-change link cannot be replayed')
    temporary_users.remove(created_email)
    created_email = new_address

    # Optional fixture IDs are created only in the isolated database by the test runner.
    retained = os.environ.get('TEST_COURSE_RETAINED_ID')
    unused = os.environ.get('TEST_COURSE_UNUSED_ID')
    if retained and unused:
        invalid_course = admin.post('/Admin/EditCourse/' + retained,
            {'Title': '   ', 'Description': 'D' * 1001, 'Category': 'C' * 51, 'IsPublished': 'true'})
        check(all(field_error(invalid_course[2], field) for field in ('Title', 'Category', 'Description'))
            and 'validation-summary-errors' not in invalid_course[2],
            'course review shows required/length failures beside each field without a duplicate summary')
        boundary_course = admin.post('/Admin/EditCourse/' + retained,
            {'Title': 'T' * 150, 'Description': 'D' * 1000, 'Category': 'C' * 50, 'IsPublished': 'false'})
        check('/Admin/Courses' in boundary_course[1], 'course review accepts the exact title/category/description length limits')
        edited = admin.post('/Admin/EditCourse/' + retained,
            {'Title': '  Reviewed course  ', 'Description': '  Reviewed by Admin  ', 'Category': '  Security  ', 'IsPublished': 'true'})
        check('/Admin/Courses' in edited[1] and 'Reviewed course' in edited[2], 'Admin can edit and publish another teacher\'s course')
        stored_course = admin.request('/Admin/EditCourse/' + retained)[2]
        stored_description = re.search(r'<textarea[^>]*id="Description"[^>]*>(.*?)</textarea>', stored_course, re.S)
        check('value="Reviewed course"' in stored_course and 'value="Security"' in stored_course
            and stored_description and html.unescape(stored_description.group(1)).strip() == 'Reviewed by Admin',
            'course metadata is trimmed consistently before storage')
        unpublished = admin.post('/Admin/EditCourse/' + retained,
            {'Title': 'Reviewed course', 'Description': '', 'Category': '   ', 'IsPublished': 'false'})
        filtered = admin.request('/Admin/Courses?status=draft&search=Reviewed%20course')[2]
        check('/Admin/Courses' in unpublished[1] and 'Reviewed course' in filtered and 'Uncategorised' in filtered,
            'Admin can unpublish an enrolled course and blank optional category uses its fallback')
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
    check(Client().post('/Identity/Account/ResendVerification', {'Email': test_email}, token=False)[0] == 400,
        'resend requires a CSRF token')
    for _ in range(31):
        limited = Client().post('/Identity/Account/ResendVerification', {'Email': 'bad'})
        if limited[0] == 429:
            break
    check(limited[0] == 429 and 'wait 10 minutes' in limited[2], 'email request rate limit prevents repeated submissions')
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
