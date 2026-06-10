#!/bin/bash
# Security Scan Script for Delivery Routing System
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
HAS_VULNERABILITIES=0

echo "========================================"
echo "1. Scanning .NET Backend Dependencies..."
echo "========================================"
DOTNET_DIR="$SCRIPT_DIR/../BackendApi"
if [ -d "$DOTNET_DIR" ]; then
    cd "$DOTNET_DIR"
    DOTNET_OUT=$(dotnet list BackendApi.csproj package --include-transitive --vulnerable || true)
    echo "$DOTNET_OUT"
    
    if echo "$DOTNET_OUT" | grep -E -q "High|Critical"; then
        echo -e "\e[31m[-] Dotnet vulnerability check failed: High or Critical severity vulnerabilities detected!\e[0m"
        HAS_VULNERABILITIES=1
    else
        echo -e "\e[32m[+] Dotnet vulnerability check passed.\e[0m"
    fi

    echo ""
    echo "Checking for deprecated .NET packages..."
    DEPRECATED_OUT=$(dotnet list BackendApi.csproj package --deprecated || true)
    echo "$DEPRECATED_OUT"
    if echo "$DEPRECATED_OUT" | grep -q "has the following deprecated packages"; then
        echo -e "\e[31m[-] Dotnet deprecated package check failed: Deprecated packages detected!\e[0m"
        HAS_VULNERABILITIES=1
    else
        echo -e "\e[32m[+] No deprecated .NET packages found.\e[0m"
    fi
    cd "$SCRIPT_DIR"
else
    echo "BackendApi directory not found. Skipping."
fi

echo ""
echo "========================================"
echo "2. Scanning Angular Frontend Dependencies..."
echo "========================================"
ANGULAR_DIR="$SCRIPT_DIR/../admin-dashboard"
if [ -d "$ANGULAR_DIR" ]; then
    cd "$ANGULAR_DIR"
    if ! npm audit --audit-level=high; then
        echo -e "\e[31m[-] Npm audit failed. High/Critical vulnerabilities detected!\e[0m"
        HAS_VULNERABILITIES=1
    else
        echo -e "\e[32m[+] Npm audit passed.\e[0m"
    fi

    echo ""
    echo "Checking for outdated npm packages (Informational)..."
    npm outdated || true
    cd "$SCRIPT_DIR"
else
    echo "admin-dashboard directory not found. Skipping."
fi

echo ""
echo "========================================"
echo "3. Scanning Python AI-Engine Dependencies..."
echo "========================================"
AI_DIR="$SCRIPT_DIR/../ai-engine"
if [ -d "$AI_DIR" ]; then
    cd "$AI_DIR"
    if command -v pip-audit &> /dev/null; then
        if ! pip-audit -r requirements.txt --strict; then
            echo -e "\e[31m[-] Pip-audit failed. Vulnerabilities detected!\e[0m"
            HAS_VULNERABILITIES=1
        else
            echo -e "\e[32m[+] Pip-audit passed.\e[0m"
        fi
    else
        echo -e "\e[33m[!] pip-audit command not found. Please install it. Skipping check.\e[0m"
    fi
    cd "$SCRIPT_DIR"
else
    echo "ai-engine directory not found. Skipping."
fi

echo ""
if [ $HAS_VULNERABILITIES -ne 0 ]; then
    echo "========================================"
    echo -e "\e[31mSecurity scan failed. High/Critical vulnerabilities found.\e[0m"
    echo "========================================"
    exit 1
else
    echo "========================================"
    echo -e "\e[32mSecurity scan completed successfully. No High/Critical vulnerabilities found.\e[0m"
    echo "========================================"
    exit 0
fi
