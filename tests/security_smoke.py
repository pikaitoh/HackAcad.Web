"""Personal sessions, history, audit and Authenticator integration checks.

Use an isolated Development database with Email__DevelopmentPickupDirectory.
No SMS/email is sent. Creates and removes random local fixtures only.
python tests/security_smoke.py http://127.0.0.1:5099
"""
raise SystemExit("Legacy Admin CRUD smoke test retired. Use tests/InstructorWorkflow and current authentication suites instead.")

from datetime import datetime, timedelta, timezone
import base64
import hashlib
import hmac
import html
import http.cookiejar
import os
import re
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from email import policy
from email.parser import BytesParser
from email.utils import getaddresses
from pathlib import Path

BASE = sys.argv[1].rstrip('/')
assert urllib.parse.urlparse(BASE).hostname in ('localhost', '127.0.0.1'), 'Local isolated test instance only'
MAIL = Path(os.environ['HACKACAD_TEST_MAIL_DIR'])
PASSWORD = os.environ.get('HACKACAD_DEMO_PASSWORD', 'HackAcad@123!')
ACCOUNT = '/Identity/Account/'
checks = 0

def check(value, description):
    global checks
    assert value, description
    checks += 1
    print('PASS', description, flush=True)

class Client:
    def __init__(self, agent='Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36'):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.opener.addheaders = [('User-Agent', agent)]

    def request(self, path, data=None):
        try:
            response = self.opener.open(urllib.request.Request(BASE + path,
                data=None if data is None else urllib.parse.urlencode(data).encode()), timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        return response.status, response.geturl(), response.read().decode()

    def post(self, path, data, source=None, csrf=True):
        data = dict(data)
        if csrf:
            body = self.request(source or path)[2]
            match = re.search(r'name="__RequestVerificationToken"[^>]+value="([^"]+)"', body)
            assert match, 'Missing CSRF form at ' + (source or path)
            data['__RequestVerificationToken'] = html.unescape(match[1])
        return self.request(path, data)

    def login(self, email, password=PASSWORD):
        return self.post(ACCOUNT+'Login', {'Email':email, 'Password':password})

def edit_path(admin, email):
    body = admin.request('/Admin/Users?search='+urllib.parse.quote(email))[2]
    match = re.search('href="(/Admin/EditUser/[^"]+)"', body)
    assert match, 'Missing test fixture'
    return html.unescape(match[1])

def confirm(client, email):
    for file in sorted(MAIL.glob('*.eml'), key=lambda p:p.stat().st_mtime_ns, reverse=True):
        message=BytesParser(policy=policy.default).parsebytes(file.read_bytes())
        if email not in [a for _,a in getaddresses(message.get_all('To', []))]: continue
        link=re.search(r'https?://\S+/Identity/Account/ConfirmEmail\?\S+', message.get_body(preferencelist=('plain',)).get_content())[0]
        parsed=urllib.parse.urlparse(link)
        assert parsed.netloc==urllib.parse.urlparse(BASE).netloc
        return client.request(parsed.path+'?'+parsed.query)
    raise AssertionError('No local verification mail for test fixture')

def totp(key):
    raw=base64.b32decode(key+'='*((-len(key))%8))
    digest=hmac.new(raw, struct.pack('>Q', int(time.time())//30), hashlib.sha1).digest()
    offset=digest[-1]&15
    return str((struct.unpack('>I',digest[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)

def session_ids(body): return re.findall(r'name="sessionId" value="([a-f0-9]+)"',body)
def codes(body): return re.findall(r'<li><code>([^<]+)</code></li>',body)

def account_time(body, field):
    content=re.search(r'<dd id="'+field+r'">(.*?)</dd>',body,re.S)[1]
    found=re.search(r'datetime="([^"]+)"',content)
    return found[1] if found else None

def logs(client, **filters):
    return client.request('/Admin/LoginLogs?'+urllib.parse.urlencode(filters))


admin=Client()
fixtures=[]
suffix=uuid.uuid4().hex[:10]
main_email='security.'+suffix+'@hackacad.local'
student_email='private.'+suffix+'@hackacad.local'
target_email='audit.'+suffix+'@hackacad.local'

def create(actor,email,role,name):
    result=actor.post('/Admin/CreateUser',{'Email':email,'DisplayName':name,'Password':PASSWORD,'ConfirmPassword':PASSWORD,'Role':role})
    assert '/Admin/Users' in result[1], 'Fixture creation failed'
    fixtures.append(email)
    assert 'Email verified' in confirm(Client(),email)[2]

try:
    check('/Admin' in admin.login('demo.admin@hackacad.local')[1], 'fixture Admin can sign in')
    create(admin,main_email,'Admin','Security admin '+suffix)
    create(admin,student_email,'Student','Security student '+suffix)
    main_path=edit_path(admin,main_email)
    pristine=admin.request(main_path)[2]
    check(account_time(pristine,'last-login') is None and account_time(pristine,'last-password-change') is None, 'new account has no invented login or password-change timestamp')
    a,b,student=Client(),Client('Mozilla/5.0 (X11; Linux x86_64) Firefox/130.0'),Client('Mozilla/5.0 (Android) Chrome/130.0')
    check('/Admin' in a.login(main_email)[1] and '/Admin' in b.login(main_email)[1], 'one account can have two independent browser sessions')
    student.login(student_email)
    successful_login=account_time(admin.request(main_path)[2],'last-login')
    check(successful_login is not None and 'Last login (UTC+8)' in admin.request('/Admin/Users')[2], 'completed login is displayed in user management')
    Client().login(main_email,'Wrong@123!')
    check(account_time(admin.request(main_path)[2],'last-login')==successful_login, 'wrong password does not advance last successful login')
    global_logs=logs(admin)[2]
    check(main_email in global_logs and student_email in global_logs, 'Admin Login logs includes attempts from different users')
    check(student.request('/Admin/LoginLogs')[0]==403 and '/Login' in Client().request('/Admin/LoginLogs')[1], 'global login logs reject Student and anonymous access')
    filtered=logs(admin,search=main_email,result='failed')[2]
    check(main_email in filtered and student_email not in filtered and '>Signed in</span>' not in filtered and '>Failed</span>' in filtered, 'global logs filter by account and failed result')
    successful=logs(admin,search=main_email,result='success')[2]
    check(main_email in successful and '>Failed</span>' not in successful, 'global logs successful filter excludes failures')
    today=datetime.now(timezone(timedelta(hours=8))).date()
    check(main_email in logs(admin,search=main_email,**{'from':str(today),'to':str(today)})[2], 'same-day date filters include Malaysia sign-ins')
    check('No sign-ins to show' in logs(admin,search=main_email,**{'from':str(today+timedelta(days=1))})[2], 'future date range excludes current records')
    date_error=logs(admin,**{'from':'2026-10-20','to':'2026-10-19'})[2]
    check('Choose an end date on or after' in date_error and 'aria-invalid="true"' in date_error, 'reversed date range gives an accessible inline error')
    check('No sign-ins to show' not in date_error and 'recorded attempt(s)' not in date_error, 'invalid date filters do not claim that no records exist')
    check('workspace-note' not in global_logs and 'Account &amp; platform access' not in global_logs and 'sidebar-back' not in global_logs, 'Administration sidebar ends after its navigation')
    check(logs(admin,**{'from':'invalid'})[0]==200 and logs(admin,**{'from':'0001-01-01'})[0]==200, 'malformed and underflow dates do not crash the logs page')
    check('No sign-ins to show' in logs(admin,search='no-account-'+suffix)[2], 'unmatched search has an honest empty state')
    pw_before=account_time(student.request(ACCOUNT+'ChangePassword')[2],'last-password-change')
    bad_password=student.post(ACCOUNT+'ChangePassword',{'CurrentPassword':'Wrong@123!','NewPassword':'NewSecure@456!','ConfirmPassword':'NewSecure@456!','LastPasswordChangedUtc':'2000-01-01'})
    check('incorrect' in bad_password[2].lower() and account_time(bad_password[2],'last-password-change')==pw_before, 'failed password change and forged timestamp leave recorded time unchanged')
    updated_password='NewSecure@456!'
    changed=student.post(ACCOUNT+'ChangePassword',{'CurrentPassword':PASSWORD,'NewPassword':updated_password,'ConfirmPassword':updated_password})
    password_time=account_time(student.request(ACCOUNT+'ChangePassword')[2],'last-password-change')
    check('/Settings' in changed[1] and password_time is not None and account_time(admin.request(edit_path(admin,student_email))[2],'last-password-change')==password_time, 'successful password change persists the same timestamp in personal and Admin views')
    student.post(ACCOUNT+'ChangePassword',{'CurrentPassword':updated_password,'NewPassword':PASSWORD,'ConfirmPassword':PASSWORD})
    full_name="陈明 O'Connor"
    saved=a.post(ACCOUNT+'Profile',{'DisplayName':'Security admin '+suffix,'FullName':'  '+full_name+'  ', 'Id':edit_path(admin,student_email).rsplit('/',1)[1]})
    stored=html.unescape(a.request(ACCOUNT+'Profile')[2])
    check('Your profile has been updated' in saved[2] and 'value="'+full_name+'"' in stored,
        'Full name is trimmed and persists after loading Profile again')
    check(full_name not in html.unescape(student.request(ACCOUNT+'Profile')[2]), 'profile Full name changes cannot target another user')
    boundary=a.post(ACCOUNT+'Profile',{'DisplayName':'Security admin '+suffix,'FullName':'名'*100})
    check('Your profile has been updated' in boundary[2], 'Full name accepts the 100-character boundary')
    oversized=a.post(ACCOUNT+'Profile',{'DisplayName':'Security admin '+suffix,'FullName':'名'*101})
    check('FullName-error' in oversized[2] and 'Use up to 100 characters' in oversized[2]
        and 'value="'+'名'*100+'"' in html.unescape(a.request(ACCOUNT+'Profile')[2]), 'oversized Full name is rejected without replacing the saved name')
    cleared=a.post(ACCOUNT+'Profile',{'DisplayName':'Security admin '+suffix,'FullName':'  '})
    check('Your profile has been updated' in cleared[2] and 'value="'+'名'*100+'"' not in html.unescape(a.request(ACCOUNT+'Profile')[2]), 'Full name is optional and can be cleared')
    sessions=a.request(ACCOUNT+'Sessions')[2]
    ids=session_ids(sessions)
    check(len(ids)==1 and 'This device' in sessions and 'Firefox on Linux' in sessions and 'UTC+8' in sessions, 'sessions show the current browser, other browser and Malaysia time')
    check('Chrome on Android' not in sessions, 'sessions exclude other users')
    check(a.post(ACCOUNT+'Sessions',{'CurrentPassword':PASSWORD,'sessionId':ids[0]},csrf=False)[0]==400, 'session sign-out requires CSRF protection')
    wrong=a.post(ACCOUNT+'Sessions',{'CurrentPassword':'Wrong@123!','sessionId':ids[0]})
    check('Your current password is incorrect.' in wrong[2] and 'aria-invalid="true"' in wrong[2] and '/Admin' in b.request('/Admin')[1], 'wrong password has a clear inline error and leaves the other login valid')
    check(student.post(ACCOUNT+'Sessions',{'CurrentPassword':PASSWORD,'sessionId':ids[0]})[0]==404, 'another user cannot revoke a foreign session')
    check(a.post(ACCOUNT+'Sessions',{'CurrentPassword':PASSWORD,'sessionId':ids[0]})[0]==200, 'individual sign-out succeeds')
    check('/Login' in b.request('/Admin')[1] and '/Admin' in a.request('/Admin')[1], 'individual sign-out revokes only the selected browser')
    b.login(main_email)
    check(a.post(ACCOUNT+'Sessions',{'CurrentPassword':PASSWORD})[0]==200, 'bulk sign-out succeeds')
    check('/Login' in b.request('/Admin')[1] and len(session_ids(a.request(ACCOUNT+'Sessions')[2]))==0, 'bulk sign-out retains the current session and revokes other sessions')
    Client().login(main_email,'Wrong@123!')
    history=a.request(ACCOUNT+'LoginHistory')[2]
    check('Firefox on Linux' in history and '>Failed</span>' in history and 'Chrome on Android' not in history, 'own history records successes and failures without exposing another user')
    check('/Login' in Client().request(ACCOUNT+'LoginHistory')[1], 'anonymous users cannot open login history')
    check(student.request('/Admin/Activity')[0]==403, 'Student cannot view Admin activity')
    create(a,target_email,'Student','Audit target '+suffix)
    activity=a.request('/Admin/Activity')[2]
    check('Created account' in activity and 'Audit target '+suffix in activity, 'Admin account creation records the actor and target')
    check('Audit target '+suffix not in admin.request('/Admin/Activity')[2], 'My admin activity excludes other Admin actions')
    target_path=edit_path(a,target_email)
    a.post(target_path,{'Email':target_email,'DisplayName':'Edited target '+suffix,'Role':'Student','IsDisabled':'true'})
    activity=a.request('/Admin/Activity')[2]
    check('Updated account' in activity and 'Account disabled' in activity, 'successful role/status edits are audited')
    before=activity.count('<tr>')
    a.post(edit_path(a,main_email),{'Email':main_email,'DisplayName':'Security admin '+suffix,'Role':'Admin','IsDisabled':'true'})
    check(a.request('/Admin/Activity')[2].count('<tr>')==before, 'a rejected Admin action does not appear as successful activity')
    a.post(target_path.replace('EditUser','DeleteUser'),{'emailConfirmation':target_email})
    fixtures.remove(target_email)
    activity=a.request('/Admin/Activity')[2]
    check('Deleted account' in activity and 'Edited target '+suffix in activity and 'Audit target '+suffix in activity, 'audit snapshots survive target account deletion')
    check('Verified</span>' in a.request(ACCOUNT+'Settings')[2], 'Manage account displays actual email verification status')
    profile=a.request(ACCOUNT+'Profile')[2].split('<main',1)[1].split('</main>',1)[0]
    check('Profile picture' in profile and '>Username</label>' in profile and 'Change email' not in profile
        and 'Account security' not in profile and 'account-settings-nav' not in profile, 'View profile is separate from account security')
    for action,selected in [('Settings','Email address'),('ChangeEmail','Email address'),('PhoneNumber','Phone number'),
        ('ChangePassword','Password'),('TwoFactor','Two-factor authentication'),('LoginHistory','Login history'),('Sessions','Devices &amp; sessions')]:
        response=a.request(ACCOUNT+action)
        nav=re.search(r'<nav class="account-settings-nav".*?</nav>',response[2],re.S)
        active=re.findall(r'<a[^>]+aria-current="page"[^>]*>.*?<span>(.*?)</span>',nav[0],re.S) if nav else []
        check(response[0]==200 and active==[selected] and '>Manage account</a>' in response[2], 'shared sidebar selects the correct function: '+action)
    email_main=a.request(ACCOUNT+'Settings')[2].split('<main',1)[1].split('</main>',1)[0]
    check('Change email' in email_main and 'name="Phone.PhoneNumber"' not in email_main, 'email and phone forms are separate functions')
    student_nav=student.request(ACCOUNT+'Settings')[2]
    check('>Two-factor authentication</span>' in student_nav, 'Student account navigation offers personal 2FA setup')

    check(student.request(ACCOUNT+'TwoFactor')[0]==200, 'Student can open personal authenticator settings')
    check('/TwoFactor' in a.request(ACCOUNT+'EnableAuthenticator')[1], 'setup key is unavailable without recent password confirmation')
    wrong=a.post(ACCOUNT+'BeginAuthenticator',{'CurrentPassword':'Wrong@123!'},source=ACCOUNT+'TwoFactor')
    check('Your current password is incorrect.' in wrong[2] and 'data:image/png' not in wrong[2], 'incorrect password cannot reveal an authenticator setup key')
    setup=a.post(ACCOUNT+'BeginAuthenticator',{'CurrentPassword':PASSWORD},source=ACCOUNT+'TwoFactor')
    key=re.search(r'<code class="authenticator-key">([^<]+)</code>',setup[2])[1]
    check('data:image/png;base64,' in setup[2] and 'secret=' not in setup[2], 'QR code is rendered locally without an external QR request')
    wrong=a.post(ACCOUNT+'EnableAuthenticator',{'Code':'not-a-code'})
    check('field-validation-error' in wrong[2] and 'aria-invalid="true"' in wrong[2] and 'Save your recovery codes' not in wrong[2], 'invalid setup code cannot enable 2FA')
    enabled=a.post(ACCOUNT+'EnableAuthenticator',{'Code':totp(key)})
    first_codes=codes(enabled[2])
    check(len(first_codes)==10 and 'Save your recovery codes' in enabled[2], 'valid authenticator proof enables 2FA and returns ten recovery codes once')
    check('/Admin' in a.request('/Admin')[1] and 'authenticator-key' not in a.request(ACCOUNT+'EnableAuthenticator')[2], 'enabling 2FA retains the current browser and hides the setup secret afterward')
    before_second_factor=account_time(admin.request(main_path)[2],'last-login')
    new=Client()
    pending=new.login(main_email)
    check(account_time(admin.request(main_path)[2],'last-login')==before_second_factor, 'unfinished second factor does not advance Last login')
    check('/LoginTwoFactor' in pending[1] and '/Login' in new.request('/Admin')[1], 'correct password alone cannot access Admin when 2FA is enabled')
    failed=new.post(ACCOUNT+'LoginTwoFactor',{'Code':'000000' if totp(key)!='000000' else '111111'})
    check('That code is incorrect' in failed[2] and 'aria-invalid="true"' in failed[2], 'wrong authenticator code is rejected inline')
    success=new.post(ACCOUNT+'LoginTwoFactor',{'Code':totp(key),'ReturnUrl':'https://example.com'})
    check('/Admin' in success[1] and success[1].startswith(BASE), 'valid TOTP completes sign-in and rejects an external redirect')
    check(account_time(admin.request(main_path)[2],'last-login')!=before_second_factor, 'completed second factor advances Last login')
    recovery=Client()
    recovery.login(main_email)
    recovered=recovery.post(ACCOUNT+'LoginRecoveryCode',{'Code':first_codes[0]})
    check('/Admin' in recovered[1], 'an unused recovery code completes sign-in')
    replay=Client(); replay.login(main_email)
    check('That code is incorrect' in replay.post(ACCOUNT+'LoginRecoveryCode',{'Code':first_codes[0]})[2], 'recovery codes are single use')
    # A password-only pending login must also be revoked by sign-out-other-devices.
    pending_client=Client(); pending_client.login(main_email)
    a.post(ACCOUNT+'Sessions',{'CurrentPassword':PASSWORD})
    check('/LoginTwoFactor' not in pending_client.request(ACCOUNT+'LoginTwoFactor')[1], 'bulk sign-out invalidates an in-progress 2FA login')
    check('/Login' in new.request('/Admin')[1] and '/Login' in recovery.request('/Admin')[1], 'bulk sign-out also revokes completed 2FA sessions')
    invalid_manage=a.post(ACCOUNT+'ManageTwoFactor',{'CurrentPassword':PASSWORD,'Code':'invalid','operation':'disable'},source=ACCOUNT+'TwoFactor')
    check('unused recovery code' in invalid_manage[2] and 'aria-invalid="true"' in invalid_manage[2] and 'Turn off 2FA' in invalid_manage[2], 'turning off 2FA requires a valid second-factor proof')
    regenerated=a.post(ACCOUNT+'ManageTwoFactor',{'CurrentPassword':PASSWORD,'Code':totp(key),'operation':'regenerate'},source=ACCOUNT+'TwoFactor')
    second_codes=codes(regenerated[2])
    check(len(second_codes)==10 and set(first_codes).isdisjoint(second_codes), 'recovery-code regeneration replaces the complete previous set')
    stale=Client(); stale.login(main_email)
    check('That code is incorrect' in stale.post(ACCOUNT+'LoginRecoveryCode',{'Code':first_codes[1]})[2], 'an old unused recovery code stops working after regeneration')
    disabled=a.post(ACCOUNT+'ManageTwoFactor',{'CurrentPassword':PASSWORD,'Code':second_codes[0],'operation':'disable'},source=ACCOUNT+'TwoFactor')
    check('Two-factor authentication has been turned off' in disabled[2], 'password plus a new recovery code can turn off 2FA')
    check('/Admin' in Client().login(main_email)[1], 'disabled 2FA no longer prompts during sign-in')
    check('/LoginRecoveryCode' not in stale.request(ACCOUNT+'LoginRecoveryCode')[1], 'turning off 2FA invalidates an old pending challenge')
    check('Signed in with 2FA' in a.request(ACCOUNT+'LoginHistory')[2] and 'Verification failed' in a.request(ACCOUNT+'LoginHistory')[2], 'login history includes completed and failed second-factor checks')
    check(a.post(ACCOUNT+'Logout',{},source=ACCOUNT+'Sessions')[0]==200 and '/Login' in a.request(ACCOUNT+'Sessions')[1], 'normal logout revokes the current session')
    for _ in range(21): Client().login(student_email)
    paged=logs(admin,search=student_email,result='success',**{'from':str(today),'to':str(today)})[2]
    next_link=re.search(r'href="([^"]+)"[^>]*>Next</a>',paged)
    check(paged.count('<tr>')==21 and next_link is not None, 'global logs limit the first page to twenty attempts')
    second=admin.request(html.unescape(next_link[1]))[2]
    check(student_email in second and main_email not in second and 'Page 2 of' in second and '>Failed</span>' not in second, 'next page preserves user, result and date filters')
    check('Page 1 of' in logs(admin,search=student_email,page=-20)[2] and 'No sign-ins to show' not in logs(admin,search=student_email,page=9999)[2], 'out-of-range pages clamp to existing records')
    print(f'All {checks} security assertions passed.', flush=True)
finally:
    for email in fixtures:
        try:
            path=edit_path(admin,email).replace('EditUser','DeleteUser')
            assert '/Admin/Users' in admin.post(path,{'emailConfirmation':email})[1]
        except Exception as error:
            print('Fixture cleanup failed:',email,type(error).__name__,file=sys.stderr)
