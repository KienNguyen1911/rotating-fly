#!/bin/bash

# Exit immediately if a command exits with a non-zero status
set -e

# Color definitions for output
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
GREEN='\033[0;32m'
RED='\033[0;31m'
DARK_YELLOW='\033[0;33m'
NC='\033[0;0m' # No Color

echo -e "${CYAN}=== BẮT ĐẦU ĐÓNG GÓI ỨNG DỤNG ===${NC}"

# Nhập phiên bản
if [ -z "$1" ]; then
    read -p "Nhập phiên bản cần build (VD: 1.1): " VERSION
else
    VERSION=$1
fi

if [ -z "$VERSION" ]; then
    echo -e "${YELLOW}Chưa nhập phiên bản. Sẽ sử dụng mặc định: 1.0${NC}"
    VERSION="1.0"
fi

echo -e "${CYAN}Đang đóng gói phiên bản: v${VERSION}${NC}"

# 1. Định nghĩa các đường dẫn
PROJECT_ROOT=$(pwd)
UI_PROJECT_PATH="$PROJECT_ROOT/src/AssetAutomator.UI/AssetAutomator.UI.csproj"
PUBLISH_DIR="$PROJECT_ROOT/src/AssetAutomator.UI/bin/Release/net10.0-windows/win-x64/publish"
PACKAGE_OUT_DIR="$PROJECT_ROOT/dist_package"
ZIP_FILE="$PROJECT_ROOT/AssetAutomator_v${VERSION}.zip"

# 2. Kiểm tra file csproj tồn tại
if [ ! -f "$UI_PROJECT_PATH" ]; then
    echo -e "${RED}Lỗi: Không tìm thấy file project tại: $UI_PROJECT_PATH${NC}"
    exit 1
fi

# 3. Xóa các thư mục build cũ nếu có
if [ -d "$PACKAGE_OUT_DIR" ]; then
    echo -e "${YELLOW}Đang dọn dẹp thư mục package cũ...${NC}"
    rm -rf "$PACKAGE_OUT_DIR"
fi

if [ -f "$ZIP_FILE" ]; then
    rm -f "$ZIP_FILE"
fi

# 4. Chạy lệnh dotnet publish
echo -e "${GREEN}Đang build và publish ứng dụng...${NC}"
dotnet publish "$UI_PROJECT_PATH" -c Release -r win-x64 --self-contained true -p:Version=$VERSION

# 5. Tạo thư mục package mới và sao chép bản build sang (bao gồm cả thư mục ẩn .playwright)
echo -e "${GREEN}Đang chuẩn bị thư mục package...${NC}"
mkdir -p "$PACKAGE_OUT_DIR"
cp -r "$PUBLISH_DIR"/. "$PACKAGE_OUT_DIR/"

# 6. Dọn dẹp cache và thông tin cá nhân
echo -e "${YELLOW}Đang dọn dẹp các tệp cấu hình cá nhân và cache...${NC}"
for file in appsettings.json ai84_api_key.txt image_api_key.txt image_api_url.txt manual-proxies.txt; do
    if [ -f "$PACKAGE_OUT_DIR/$file" ]; then
        rm -f "$PACKAGE_OUT_DIR/$file"
        echo -e "  ${DARK_YELLOW}Đã xóa: $file${NC}"
    fi
done

# Xóa thư mục tạm TempProfile_ sinh ra trong quá trình chạy
rm -rf "$PACKAGE_OUT_DIR/TempProfile_"*

# 7. Tạo file nén zip
echo -e "${GREEN}Đang tạo file nén zip...${NC}"
if command -v zip &> /dev/null; then
    cd "$PACKAGE_OUT_DIR"
    zip -r "$ZIP_FILE" . * > /dev/null
    cd "$PROJECT_ROOT"
elif command -v powershell.exe &> /dev/null; then
    # Chuyển đổi đường dẫn sang định dạng Windows cho PowerShell
    if command -v cygpath &> /dev/null; then
        WIN_PACKAGE_OUT_DIR=$(cygpath -w "$PACKAGE_OUT_DIR")
        WIN_ZIP_FILE=$(cygpath -w "$ZIP_FILE")
    else
        WIN_PACKAGE_OUT_DIR="$PACKAGE_OUT_DIR"
        WIN_ZIP_FILE="$ZIP_FILE"
    fi
    powershell.exe -Command "Compress-Archive -Path '${WIN_PACKAGE_OUT_DIR}\*' -DestinationPath '${WIN_ZIP_FILE}' -Force"
else
    echo -e "${YELLOW}Cảnh báo: Không tìm thấy lệnh 'zip' hoặc 'powershell.exe' để nén file.${NC}"
    echo -e "${YELLOW}Các tệp đã được build thành công tại: $PACKAGE_OUT_DIR${NC}"
    exit 0
fi

echo ""
echo -e "${CYAN}=== ĐÓNG GÓI HOÀN TẤT ===${NC}"
echo -e "${CYAN}File đóng gói tại: $ZIP_FILE${NC}"

# 8. Cập nhật file update.xml cho AutoUpdater
echo -e "${GREEN}Đang cập nhật file update.xml cho AutoUpdater...${NC}"
cat > "$PROJECT_ROOT/update.xml" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>${VERSION}</version>
    <url>https://github.com/KienNguyen1911/AssetAutomator-Releases/releases/download/v${VERSION}/AssetAutomator_v${VERSION}.zip</url>
    <changelog>https://github.com/KienNguyen1911/AssetAutomator-Releases/releases/tag/v${VERSION}</changelog>
    <mandatory>false</mandatory>
</item>
EOF
echo -e "${CYAN}Đã tạo file update.xml (nhớ commit và push file này nhé!).${NC}"
