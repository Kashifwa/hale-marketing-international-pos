[Setup]
AppName=Hale Marketing International
AppVersion=1.0.0
DefaultDirName={userpf}\HaleMarketingInternational
DefaultGroupName=Hale Marketing International
OutputDir=C:\Users\kashi\source\repos\Hale Marketing International\Output
OutputBaseFilename=HaleMarketingInternational_Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
UsedUserAreasWarning=no
UninstallDisplayIcon={app}\Hale Marketing International.exe

[Files]
Source: "C:\Users\kashi\source\repos\Hale Marketing International\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{userprograms}\Hale Marketing International"; Filename: "{app}\Hale Marketing International.exe"
Name: "{userdesktop}\Hale Marketing International"; Filename: "{app}\Hale Marketing International.exe"

[Run]
Filename: "{app}\Hale Marketing International.exe"; Description: "Launch app"; Flags: nowait postinstall skipifsilent