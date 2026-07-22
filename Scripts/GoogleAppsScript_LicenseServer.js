/**
 * Google Apps Script - License Server Web App for AssetAutomator
 * 
 * Instructions:
 * 1. Open Google Sheets -> Extensions -> Apps Script.
 * 2. Paste this code into Code.gs (replace any existing code).
 * 3. Click 'Save' (Ctrl+S).
 * 4. Click 'Deploy' -> 'New deployment'.
 * 5. Select type: 'Web app'.
 * 6. Execute as: 'Me'.
 * 7. Who has access: 'Anyone'.
 * 8. Click 'Deploy' and copy the Web App URL.
 */

const SHEET_LICENSES = "Licenses";
const SHEET_LOGS = "ActivationLogs";
const LICENSE_DURATION_DAYS = 30;

function doGet(e) {
  return ContentService.createTextOutput(JSON.stringify({
    status: "online",
    service: "AssetAutomator License Server",
    time: new Date().toISOString()
  })).setMimeType(ContentService.MimeType.JSON);
}

function doPost(e) {
  try {
    const postData = JSON.parse(e.postData.contents);
    const action = (postData.action || "").toLowerCase();
    
    setupSheetsIfMissing();
    
    switch (action) {
      case "activate":
        return handleActivate(postData);
      case "verify":
        return handleVerify(postData);
      case "heartbeat":
        return handleHeartbeat(postData);
      case "deactivate":
        return handleDeactivate(postData);
      case "transfer":
        return handleTransfer(postData);
      default:
        return responseJSON({ success: false, message: "Invalid action" });
    }
  } catch (err) {
    return responseJSON({ success: false, message: "Server error: " + err.toString() });
  }
}

function setupSheetsIfMissing() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  
  let licenseSheet = ss.getSheetByName(SHEET_LICENSES);
  if (!licenseSheet) {
    licenseSheet = ss.insertSheet(SHEET_LICENSES);
    licenseSheet.appendRow([
      "LicenseKeyHash", "LicenseKey", "AppId", "Status", "DeviceId", 
      "ActivatedAt", "ExpiredAt", "LastHeartbeat", "SessionId", "TransferCount"
    ]);
  }
  
  let logSheet = ss.getSheetByName(SHEET_LOGS);
  if (!logSheet) {
    logSheet = ss.insertSheet(SHEET_LOGS);
    logSheet.appendRow(["Timestamp", "LicenseKey", "DeviceId", "Action", "Status", "Details"]);
  }
}

function handleActivate(data) {
  const { licenseKey, deviceId, appId } = data;
  if (!licenseKey || !deviceId) {
    return responseJSON({ success: false, message: "Missing licenseKey or deviceId" });
  }

  const keyHash = hashString(licenseKey);
  const row = findLicenseRow(keyHash, licenseKey);
  
  if (!row) {
    logAction(licenseKey, deviceId, "ACTIVATE", "FAILED", "License key not found");
    return responseJSON({ success: false, message: "License key is invalid or does not exist." });
  }

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const status = sheet.getRange(row, 4).getValue();
  const currentDeviceId = sheet.getRange(row, 5).getValue();
  
  if (status === "Revoked") {
    logAction(licenseKey, deviceId, "ACTIVATE", "FAILED", "License revoked");
    return responseJSON({ success: false, message: "License has been revoked." });
  }

  const now = new Date();

  // If already bound to another device
  if (currentDeviceId && currentDeviceId !== deviceId) {
    logAction(licenseKey, deviceId, "ACTIVATE", "FAILED", "Already bound to another device");
    return responseJSON({ 
      success: false, 
      code: "ALREADY_BOUND", 
      message: "License is bound to a different device. Use Transfer option if needed." 
    });
  }

  // Activate first time or re-activate same device
  let activatedAt = sheet.getRange(row, 6).getValue();
  let expiredAt = sheet.getRange(row, 7).getValue();

  if (!activatedAt || !expiredAt) {
    activatedAt = now;
    expiredAt = new Date(now.getTime() + LICENSE_DURATION_DAYS * 24 * 60 * 60 * 1000);
    sheet.getRange(row, 6).setValue(activatedAt.toISOString());
    sheet.getRange(row, 7).setValue(expiredAt.toISOString());
  } else {
    expiredAt = new Date(expiredAt);
    if (now > expiredAt) {
      sheet.getRange(row, 4).setValue("Expired");
      logAction(licenseKey, deviceId, "ACTIVATE", "FAILED", "License expired");
      return responseJSON({ success: false, code: "EXPIRED", message: "License has expired." });
    }
  }

  const sessionId = Utilities.getUuid();
  sheet.getRange(row, 3).setValue(appId || "AssetAutomator");
  sheet.getRange(row, 4).setValue("Active");
  sheet.getRange(row, 5).setValue(deviceId);
  sheet.getRange(row, 8).setValue(now.toISOString());
  sheet.getRange(row, 9).setValue(sessionId);

  logAction(licenseKey, deviceId, "ACTIVATE", "SUCCESS", "Activated successfully");

  const remainingDays = Math.ceil((expiredAt - now) / (1000 * 60 * 60 * 24));
  return responseJSON({
    success: true,
    status: "Active",
    sessionId: sessionId,
    activatedAt: new Date(activatedAt).toISOString(),
    expiredAt: expiredAt.toISOString(),
    remainingDays: remainingDays,
    message: "License activated successfully."
  });
}

function handleVerify(data) {
  const { licenseKey, deviceId, sessionId } = data;
  if (!licenseKey || !deviceId) {
    return responseJSON({ success: false, message: "Missing licenseKey or deviceId" });
  }

  const keyHash = hashString(licenseKey);
  const row = findLicenseRow(keyHash, licenseKey);

  if (!row) {
    return responseJSON({ success: false, code: "INVALID_KEY", message: "License key not found." });
  }

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const status = sheet.getRange(row, 4).getValue();
  const currentDeviceId = sheet.getRange(row, 5).getValue();
  const currentSessionId = sheet.getRange(row, 9).getValue();
  const expiredAtStr = sheet.getRange(row, 7).getValue();

  if (status === "Revoked") {
    return responseJSON({ success: false, code: "REVOKED", message: "License has been revoked." });
  }

  if (currentDeviceId !== deviceId) {
    return responseJSON({ success: false, code: "DEVICE_MISMATCH", message: "License is bound to another device." });
  }

  if (sessionId && currentSessionId && sessionId !== currentSessionId) {
    return responseJSON({ success: false, code: "INVALID_SESSION", message: "Session invalid. License in use elsewhere." });
  }

  const now = new Date();
  const expiredAt = new Date(expiredAtStr);
  if (now > expiredAt) {
    sheet.getRange(row, 4).setValue("Expired");
    return responseJSON({ success: false, code: "EXPIRED", message: "License has expired." });
  }

  const remainingDays = Math.ceil((expiredAt - now) / (1000 * 60 * 60 * 24));
  sheet.getRange(row, 8).setValue(now.toISOString());

  return responseJSON({
    success: true,
    status: "Active",
    sessionId: currentSessionId,
    expiredAt: expiredAt.toISOString(),
    remainingDays: remainingDays
  });
}

function handleHeartbeat(data) {
  const { licenseKey, deviceId, sessionId } = data;
  if (!licenseKey || !deviceId || !sessionId) {
    return responseJSON({ success: false, message: "Missing parameters for heartbeat" });
  }

  const keyHash = hashString(licenseKey);
  const row = findLicenseRow(keyHash, licenseKey);
  if (!row) {
    return responseJSON({ success: false, code: "INVALID_KEY", message: "License key not found" });
  }

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const currentDeviceId = sheet.getRange(row, 5).getValue();
  const currentSessionId = sheet.getRange(row, 9).getValue();
  const expiredAtStr = sheet.getRange(row, 7).getValue();
  const status = sheet.getRange(row, 4).getValue();

  if (status === "Revoked") {
    return responseJSON({ success: false, code: "REVOKED", message: "License revoked" });
  }

  if (currentDeviceId !== deviceId || currentSessionId !== sessionId) {
    logAction(licenseKey, deviceId, "HEARTBEAT", "REJECTED", "INVALID_SESSION - License active on another device");
    return responseJSON({ success: false, code: "INVALID_SESSION", message: "License is currently active on another device." });
  }

  const now = new Date();
  if (new Date(expiredAtStr) < now) {
    sheet.getRange(row, 4).setValue("Expired");
    return responseJSON({ success: false, code: "EXPIRED", message: "License expired" });
  }

  sheet.getRange(row, 8).setValue(now.toISOString());
  return responseJSON({ success: true, status: "Active" });
}

function handleDeactivate(data) {
  const { licenseKey, deviceId } = data;
  const keyHash = hashString(licenseKey);
  const row = findLicenseRow(keyHash, licenseKey);

  if (!row) {
    return responseJSON({ success: false, message: "License key not found" });
  }

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const currentDeviceId = sheet.getRange(row, 5).getValue();

  if (currentDeviceId && currentDeviceId !== deviceId) {
    return responseJSON({ success: false, message: "Device mismatch. Cannot deactivate." });
  }

  sheet.getRange(row, 5).setValue(""); // Clear DeviceId
  sheet.getRange(row, 9).setValue(""); // Clear SessionId
  logAction(licenseKey, deviceId, "DEACTIVATE", "SUCCESS", "Device unbinded successfully");

  return responseJSON({ success: true, message: "License deactivated on this device." });
}

function handleTransfer(data) {
  const { licenseKey, newDeviceId } = data;
  if (!licenseKey || !newDeviceId) {
    return responseJSON({ success: false, message: "Missing licenseKey or newDeviceId" });
  }

  const keyHash = hashString(licenseKey);
  const row = findLicenseRow(keyHash, licenseKey);
  if (!row) {
    return responseJSON({ success: false, message: "License key not found" });
  }

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const status = sheet.getRange(row, 4).getValue();
  const expiredAtStr = sheet.getRange(row, 7).getValue();

  if (status === "Revoked") {
    return responseJSON({ success: false, message: "License revoked." });
  }

  const now = new Date();
  if (expiredAtStr && new Date(expiredAtStr) < now) {
    return responseJSON({ success: false, message: "License expired. Cannot transfer." });
  }

  const newSessionId = Utilities.getUuid();
  const currentTransfers = parseInt(sheet.getRange(row, 10).getValue() || 0, 10);

  sheet.getRange(row, 4).setValue("Active");
  sheet.getRange(row, 5).setValue(newDeviceId);
  sheet.getRange(row, 8).setValue(now.toISOString());
  sheet.getRange(row, 9).setValue(newSessionId);
  sheet.getRange(row, 10).setValue(currentTransfers + 1);

  logAction(licenseKey, newDeviceId, "TRANSFER", "SUCCESS", `Transferred to new device (Count: ${currentTransfers + 1})`);

  const expiredAt = new Date(expiredAtStr);
  const remainingDays = Math.ceil((expiredAt - now) / (1000 * 60 * 60 * 24));

  return responseJSON({
    success: true,
    status: "Active",
    sessionId: newSessionId,
    expiredAt: expiredAt.toISOString(),
    remainingDays: remainingDays,
    message: "License transferred successfully to new device."
  });
}

function findLicenseRow(keyHash, rawKey) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  const data = sheet.getDataRange().getValues();
  
  for (let i = 1; i < data.length; i++) {
    const hashInSheet = data[i][0];
    const keyInSheet = data[i][1];
    
    if (hashInSheet === keyHash || keyInSheet === rawKey) {
      return i + 1; // 1-indexed row
    }
  }
  return null;
}

function hashString(str) {
  const digest = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, str + "_Salt_AssetAutomator");
  return digest.map(byte => (byte < 0 ? byte + 256 : byte).toString(16).padStart(2, '0')).join('');
}

function logAction(licenseKey, deviceId, action, status, details) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LOGS);
  sheet.appendRow([new Date().toISOString(), licenseKey, deviceId, action, status, details]);
}

function responseJSON(data) {
  return ContentService.createTextOutput(JSON.stringify(data)).setMimeType(ContentService.MimeType.JSON);
}

/**
 * Custom Menu in Google Sheets to easily generate License Keys
 */
function onOpen() {
  try {
    const ui = SpreadsheetApp.getUi();
    ui.createMenu('🔑 Quản Lý License')
      .addItem('➕ Tạo 1 Key Mới', 'generateSingleLicenseKey')
      .addItem('📦 Tạo Hàng Loạt Key', 'generateBatchLicenseKeys')
      .addToUi();
  } catch (e) {
    // Ignore if running outside Sheet UI context
  }
}

function generateSingleLicenseKey() {
  setupSheetsIfMissing();
  const key = createRandomKey();
  const keyHash = hashString(key);

  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
  sheet.appendRow([keyHash, key, "AssetAutomator", "Inactive", "", "", "", "", "", 0]);

  SpreadsheetApp.getUi().alert(`🎉 Tạo Key thành công!\n\nLicense Key: ${key}`);
}

function generateBatchLicenseKeys() {
  setupSheetsIfMissing();
  const ui = SpreadsheetApp.getUi();
  const response = ui.prompt('Tạo Hàng Loạt License Key', 'Nhập số lượng Key muốn tạo (từ 1 đến 50):', ui.ButtonSet.OK_CANCEL);

  if (response.getSelectedButton() === ui.Button.OK) {
    const count = parseInt(response.getResponseText(), 10);
    if (isNaN(count) || count <= 0 || count > 50) {
      ui.alert('Số lượng không hợp lệ (vui lòng nhập từ 1 đến 50).');
      return;
    }

    const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_LICENSES);
    const createdKeys = [];

    for (let i = 0; i < count; i++) {
      const key = createRandomKey();
      const keyHash = hashString(key);
      sheet.appendRow([keyHash, key, "AssetAutomator", "Inactive", "", "", "", "", "", 0]);
      createdKeys.push(key);
    }

    ui.alert(`🎉 Đã tạo thành công ${count} License Key:\n\n` + createdKeys.join('\n'));
  }
}

function createRandomKey() {
  const part1 = Utilities.getUuid().substring(0, 4).toUpperCase();
  const part2 = Utilities.getUuid().substring(0, 4).toUpperCase();
  const part3 = Utilities.getUuid().substring(0, 4).toUpperCase();
  return `ASSET-${part1}-${part2}-${part3}`;
}

