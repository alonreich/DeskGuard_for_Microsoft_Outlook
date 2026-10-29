; ==================================================================================================
; NODROPPER_01 - DESKGUARD FOR MICROSOFT OUTLOOK INSTALLER (Inno Setup 6.3+)
;
; Antivirus-friendly container format: every payload file is inspected natively by Windows Defender.
; Runs with PrivilegesRequired=lowest, unpacks to {tmp}\deskguard, and executes the transactional installer:
;   {tmp}\deskguard\DeskGuard.exe --install --smart-update "{tmp}\deskguard"
; ==================================================================================================

#if Ver < EncodeVer(6, 3, 0)
  #error Inno Setup 6.3 or newer is required.
#endif
#ifndef AppVersion
  #error AppVersion is missing. Build via dev_build.cmd / DeskGuard.Build.
#endif
#ifndef StagingDir
  #error StagingDir is missing. Build via dev_build.cmd / DeskGuard.Build.
#endif
#ifndef SignedListDir
  #error SignedListDir is missing. Build via dev_build.cmd / DeskGuard.Build.
#endif
#ifndef IconFile
  #error IconFile is missing. Build via dev_build.cmd / DeskGuard.Build.
#endif
#ifndef OutputDir
  #error OutputDir is missing. Build via dev_build.cmd / DeskGuard.Build.
#endif

[Setup]
AppName=DeskGuard for Microsoft Outlook
AppVersion={#AppVersion}
AppVerName=DeskGuard for Microsoft Outlook {#AppVersion}
AppPublisher=DeskGuard
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=DeskGuard
VersionInfoDescription=DeskGuard for Microsoft Outlook Setup
VersionInfoProductName=DeskGuard for Microsoft Outlook
VersionInfoOriginalFileName=DeskGuard.exe
OutputDir={#OutputDir}
OutputBaseFilename=DeskGuard
SetupIconFile={#IconFile}
PrivilegesRequired=lowest
CreateAppDir=no
Uninstallable=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
DisableProgramGroupPage=yes
DisableStartupPrompt=yes
ShowLanguageDialog=no
WizardStyle=modern
ArchitecturesAllowed=x64compatible
MinVersion=10.0
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=no
RestartIfNeededByRun=no
SetupLogging=no

[Messages]
StatusExtractFiles=Unpacking DeskGuard for Microsoft Outlook…

[Files]
Source: "{#StagingDir}\*"; DestDir: "{tmp}\deskguard"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SignedListDir}\update.json"; DestDir: "{tmp}\deskguard"; Flags: ignoreversion
Source: "{#SignedListDir}\update.json.sig"; DestDir: "{tmp}\deskguard"; Flags: ignoreversion

[Run]
Filename: "{tmp}\deskguard\DeskGuard.exe"; Parameters: "--install --smart-update ""{tmp}\deskguard"""; WorkingDir: "{tmp}\deskguard"; StatusMsg: "Starting the DeskGuard installer…"; Flags: waituntilterminated runasoriginaluser
