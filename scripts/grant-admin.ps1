#!/usr/bin/env pwsh
# Script to grant admin access by adding a test claim or creating a local admin user
# Usage: .\scripts\grant-admin.ps1 -Email "user@example.com"

param(
    [Parameter(Mandatory=$true)]
    [string]$Email
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Grant Admin Access" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$dbPath = ".\src\SamlScimReference.Web\data\app.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "❌ Database not found at: $dbPath" -ForegroundColor Red
    Write-Host "   Please run the application first to create the database." -ForegroundColor Yellow
    exit 1
}

Write-Host "📝 Note: Admin access is controlled by SAML role claims." -ForegroundColor Yellow
Write-Host "   To grant admin access, you need to:" -ForegroundColor Yellow
Write-Host ""
Write-Host "   1. Configure Azure AD to send 'Admin' role claim for user: $Email" -ForegroundColor White
Write-Host "      - In Azure AD, go to Enterprise Applications > Your App" -ForegroundColor Gray
Write-Host "      - Single sign-on > Attributes & Claims" -ForegroundColor Gray
Write-Host "      - Add a 'role' claim with value 'Admin' for this user" -ForegroundColor Gray
Write-Host ""
Write-Host "   2. Or for local development, you can bypass auth by setting an environment variable:" -ForegroundColor White
Write-Host "      $`env:BYPASS_AUTH=`"true`"" -ForegroundColor Gray
Write-Host ""

# Check if user exists in database
$query = "SELECT Id, Email, UserName, Active FROM Users WHERE Email = '$Email' COLLATE NOCASE;"

Write-Host "Checking if user exists in database..." -ForegroundColor Cyan

# Use sqlite3 if available, otherwise show manual instructions
if (Get-Command sqlite3 -ErrorAction SilentlyContinue) {
    $result = sqlite3 $dbPath $query
    if ($result) {
        Write-Host "✅ User found in database:" -ForegroundColor Green
        Write-Host $result
    } else {
        Write-Host "⚠️  User not found in database." -ForegroundColor Yellow
        Write-Host "   User must be provisioned via SCIM first." -ForegroundColor Yellow
    }
} else {
    Write-Host "ℹ️  Install sqlite3 to check user status automatically." -ForegroundColor Blue
    Write-Host "   Manual query: $query" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "For production, use Azure AD role assignment." -ForegroundColor Yellow
Write-Host "For local dev, use SQLite and bypass auth." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
