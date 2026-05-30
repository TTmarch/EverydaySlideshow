#define AppName "Everyday Slideshow"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\publish\portable-win-x64"
#endif
#ifndef RootDir
#define RootDir ".."
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif
#ifndef OutputBaseFilename
#define OutputBaseFilename "EverydaySlideshow-0.1.0-setup-win-x64"
#endif

[Setup]
AppId={{98F052C7-C0D8-4E50-B82F-10B0EF3AF7A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Everyday Slideshow contributors
AppPublisherURL=https://github.com/
AppSupportURL=https://github.com/
AppUpdatesURL=https://github.com/
DefaultDirName={autopf}\Everyday Slideshow
DefaultGroupName=Everyday Slideshow
DisableProgramGroupPage=yes
LicenseFile={#RootDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#RootDir}\src\EverydaySlideshow\Assets\app.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\EverydaySlideshow.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\EverydaySlideshow.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RootDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RootDir}\README.ja.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RootDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Everyday Slideshow"; Filename: "{app}\EverydaySlideshow.exe"
Name: "{group}\Uninstall Everyday Slideshow"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Everyday Slideshow"; Filename: "{app}\EverydaySlideshow.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EverydaySlideshow.exe"; Description: "{cm:LaunchProgram,Everyday Slideshow}"; Flags: nowait postinstall skipifsilent
