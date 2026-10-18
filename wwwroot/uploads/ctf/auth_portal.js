// HackAcad Client Authentication Portal Script v1.4
function authenticateUser(username, password) {
    if (!username || !password) {
        alert("Please enter credentials.");
        return false;
    }
    
    // Insecure client-side verification
    var adminToken = "ZmxhZ3tjbDEzbnRfczFkM192NGwxZDR0MTBuX2Y0MWx9"; // Base64 token
    var decodedFlag = atob(adminToken); // flag{cl13nt_s1d3_v4l1d4t10n_f41l}
    
    if (username === "admin" && password === "SuperSecret123!") {
        console.log("Access Granted! Verification Token: " + decodedFlag);
        return true;
    }
    return false;
}
