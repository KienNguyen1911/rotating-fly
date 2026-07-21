#!/bin/bash

# Exit immediately if a command exits with a non-zero status
set -e

# Color definitions for output
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
GREEN='\033[0;32m'
NC='\033[0;0m' # No Color

echo -e "${CYAN}=== BẮT ĐẦU ĐÓNG GÓI ỨNG DỤNG v1.1 ===${NC}"

# 1. Định nghĩa các đường dẫn
PROJECT_ROOT=$(pwd)
PUBLISH_DIR="$PROJECT_ROOT/bin/Release/net10.0-windows/win-x64/publish"
PACKAGE_OUT_DIR="$PROJECT_ROOT/dist_package"
ZIP_FILE="$PROJECT_ROOT/AssetAutomator_v1.1.zip"

# 2. Xóa các thư mục build cũ nếu có
if [ -d "$PACKAGE_OUT_DIR" ]; then
    echo -e "${YELLOW}Đang dọn dẹp thư mục package cũ...${NC}"
    rm -rf "$PACKAGE_OUT_DIR"
fi

if [ -f "$ZIP_FILE" ]; then
    rm -f "$ZIP_FILE"
fi

# 3. Chạy lệnh dotnet publish
echo -e "${GREEN}Đang build và publish ứng dụng...${NC}"
dotnet publish AssetAutomator.csproj -c Release -r win-x64 --self-contained true

# 4. Tạo thư mục package mới và sao chép bản build sang (bao gồm cả thư mục ẩn .playwright)
echo -e "${GREEN}Đang chuẩn bị thư mục package...${NC}"
mkdir -p "$PACKAGE_OUT_DIR"
cp -r "$PUBLISH_DIR"/. "$PACKAGE_OUT_DIR/"

# 5. Tạo file nén zip
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

echo -e "${CYAN}=== ĐÓNG GÓI HOÀN TẤT ===${NC}"
echo -e "${CYAN}File đóng gói tại: $ZIP_FILE${NC}"
