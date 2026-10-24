# ساختار پروژه و راهنمای ساخت

کد منبع MadNegar و ابزارهای ساخت آن برای ویندوز در این مخزن قرار دارند. راهنمای کامل‌تر انگلیسی در [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) نگهداری می‌شود.

- `src/*.cs`: رابط کاربری، تنظیمات، ضبط و پردازش صوت، اتصال به موتورهای گفتار، تاریخچه و ابزارهای متن.
- `MadNegar.csproj` و `MadNegar.ico`: فایل پروژهٔ .NET و آیکون برنامه.
- `FasterWhisperAdapter.py`: آداپتور Python که در پروژه جاسازی می‌شود.
- `Build-Release.cmd` و `build-release.ps1`: ساخت فایل اجرایی مستقل.
- `Build-Installer.cmd`، `Build-Installer.ps1` و `installer/MadNegar.iss`: ساخت نصب‌کننده.
- `tests/`: تست‌های Python، بررسی‌های ساختار کد C# و harness تست متن فارسی.
- `.github/workflows/`: گردش‌کارهای CI، اعتبارسنجی ویندوز و انتشار.

برای ساخت از ریشهٔ مخزن، Windows 10/11 x64 و .NET 10 SDK لازم است. برای جزئیات دستورها و خروجی‌های مورد انتظار، [راهنمای انگلیسی ساخت](PROJECT_STRUCTURE.md) و بخش «Build from source» در [README](../README.md) را ببینید.
