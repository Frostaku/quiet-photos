param([string]$AppDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$AppDirectory) { $AppDirectory = Join-Path $projectRoot 'dist\QuietPhotos' }
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$work = Join-Path $projectRoot 'obj\installer'
$dist = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'dist' }
New-Item -ItemType Directory -Path $work,$dist -Force | Out-Null
$msiPath = Join-Path $dist 'QuietPhotos-Setup.msi'
if (Test-Path -LiteralPath $msiPath) { Remove-Item -LiteralPath $msiPath }
$productCode = '{9AB8A696-7047-4A72-B46E-FB18A25D2DBE}'
$upgradeCode = '{F02BCBF0-D445-4F56-9FD6-9EDE83591D7B}'
& (Join-Path $PSScriptRoot 'build-photos-choice.ps1') -OutputDirectory $work
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath, 3)
function Invoke-Sql([string]$Sql) {
    $view = $database.OpenView($Sql)
    try { $view.Execute() } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view) }
}
function Add-Row([string]$Table, [string[]]$Columns, [object[]]$Values, [int]$StreamColumn = -1) {
    $quotedColumns = ($Columns | ForEach-Object { '`' + $_ + '`' }) -join ','
    $parameters = (@('?') * $Columns.Count) -join ','
    $view = $database.OpenView(('INSERT INTO `{0}` ({1}) VALUES ({2})' -f $Table,$quotedColumns,$parameters))
    $record = $installer.CreateRecord($Columns.Count)
    try {
        for ($i = 0; $i -lt $Values.Count; $i++) {
            if ($null -eq $Values[$i]) { continue }
            if ($i -eq $StreamColumn) { $record.SetStream($i + 1, [string]$Values[$i]); continue }
            $property = if ($Values[$i] -is [int]) { 'IntegerData' } else { 'StringData' }
            [void]$record.GetType().InvokeMember($property, [Reflection.BindingFlags]::SetProperty, $null, $record, @(($i + 1), $Values[$i]))
        }
        $view.Execute($record)
    } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view) }
}
$schemas = @(
    'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)',
    'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)',
    'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)',
    'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)',
    'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)',
    'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)',
    'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)',
    'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)',
    'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)',
    'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)',
    'CREATE TABLE `Icon` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)',
    'CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)',
    'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
    'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
    'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)',
    'CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)',
    'CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)',
    'CREATE TABLE `Signature` (`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`)',
    'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255) PRIMARY KEY `Action`)',
    'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` SHORT NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)',
    'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50), `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)',
    'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(50), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)',
    'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)',
    'CREATE TABLE `CheckBox` (`Property` CHAR(72) NOT NULL, `Value` CHAR(64) LOCALIZABLE PRIMARY KEY `Property`)',
    'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)',
    'CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)',
    'CREATE TABLE `ActionText` (`Action` CHAR(72) NOT NULL, `Description` CHAR(64) LOCALIZABLE, `Template` CHAR(128) LOCALIZABLE PRIMARY KEY `Action`)',
    'CREATE TABLE `Error` (`Error` SHORT NOT NULL, `Message` CHAR(0) LOCALIZABLE PRIMARY KEY `Error`)'
)
foreach ($schema in $schemas) { Invoke-Sql $schema }
$properties = [ordered]@{
    ProductCode=$productCode; UpgradeCode=$upgradeCode; ProductName='Quiet Photos'; ProductVersion='1.1.1'; ProductLanguage='1033'; Manufacturer='Quiet Photos';
    ALLUSERS='2'; MSIINSTALLPERUSER='1'; INSTALLLEVEL='1'; DESKTOP_SHORTCUT='1'; ARPPRODUCTICON='QuietPhotosIcon'; ARPNOMODIFY='1';
    ARPCOMMENTS='Compact local image viewer'; DefaultUIFont='Body'; ErrorDialog='ErrorDialog'; SecureCustomProperties='OLDPRODUCTS;NEWERPRODUCTS;DESKTOP_SHORTCUT;MSIINSTALLPERUSER;QPMENUROOT;QPDESKTOPROOT';
    MSIRESTARTMANAGERCONTROL='Disable'; REBOOT='ReallySuppress'
}
foreach ($entry in $properties.GetEnumerator()) { Add-Row Property @('Property','Value') @($entry.Key,$entry.Value) }
$directories = @(
    @('TARGETDIR',$null,'SourceDir'), @('LocalAppDataFolder','TARGETDIR','.'), @('ProgramsDir','LocalAppDataFolder','Programs'), @('INSTALLDIR','ProgramsDir','QuietPhotos'),
    @('DOCSDIR','INSTALLDIR','docs'), @('CODECDIR','INSTALLDIR','codecs'), @('ProgramMenuFolder','TARGETDIR','.'), @('AppMenuDir','ProgramMenuFolder','Quiet Photos'), @('DesktopFolder','TARGETDIR','.')
)
foreach ($row in $directories) { Add-Row Directory @('Directory','Directory_Parent','DefaultDir') $row }
Add-Row Feature @('Feature','Title','Description','Display','Level','Directory_','Attributes') @('Main','Quiet Photos','Local image viewer and shortcuts',1,1,'INSTALLDIR',0)
$components = @(
    @('AppComponent','{24EDC17D-FA63-4F94-BB82-4DFE390EEBDA}','INSTALLDIR',260,$null,'InstallMarker'),
    @('DocsComponent','{2FA7B5D6-5C81-4C48-8D72-14B95122E3CF}','DOCSDIR',260,$null,'DocsMarker'),
    @('CodecComponent','{6EEC121A-2838-4C85-BB30-97D5148B0684}','CODECDIR',260,$null,'CodecMarker'),
    @('DesktopComponent','{8B93E7E4-EB96-4CC3-858C-0BBFC46DD542}','DesktopFolder',260,'DESKTOP_SHORTCUT = 1','DesktopMarker')
)
foreach ($row in $components) { Add-Row Component @('Component','ComponentId','Directory_','Attributes','Condition','KeyPath') $row; Add-Row FeatureComponents @('Feature_','Component_') @('Main',$row[0]) }
$payload = @(
    @('AppExe','AppComponent','QPHOTO~1.EXE|QuietPhotos.exe',(Join-Path $AppDirectory 'QuietPhotos.exe'),'1.1.1.0'),
    @('AppConfig','AppComponent','QPHOTO~1.CON|QuietPhotos.exe.config',(Join-Path $AppDirectory 'QuietPhotos.exe.config'),$null),
    @('AppReadme','AppComponent','README.md',(Join-Path $projectRoot 'README.md'),$null),
    @('AppIcon','AppComponent','QPHOTO~1.ICO|QuietPhotos.ico',(Join-Path $projectRoot 'assets\icons\QuietPhotos.ico'),$null),
    @('ReaDoc','DocsComponent','REAFIN~1.MD|rea-findings.md',(Join-Path $projectRoot 'docs\rea-findings.md'),$null),
    @('ValidationDoc','DocsComponent','VALIDA~1.MD|validation.md',(Join-Path $projectRoot 'docs\validation.md'),$null),
    @('InstallerDoc','DocsComponent','INSTAL~1.MD|installer.md',(Join-Path $projectRoot 'docs\installer.md'),$null),
    @('ResearchDoc','DocsComponent','VIEWER~1.MD|viewer-research.md',(Join-Path $projectRoot 'docs\viewer-research.md'),$null),
    @('DecoderDoc','DocsComponent','DECODE~1.MD|decoder-dependencies.md',(Join-Path $projectRoot 'docs\decoder-dependencies.md'),$null)
)
foreach ($codecFile in (Get-ChildItem -LiteralPath (Join-Path $AppDirectory 'codecs') -File | Sort-Object Name)) {
    $id = 'Codec' + ($payload.Count + 1)
    $payload += ,@($id,'CodecComponent',('C' + $payload.Count + '~1.' + $codecFile.Extension.TrimStart('.').Substring(0,[Math]::Min(3,$codecFile.Extension.Length - 1)) + '|' + $codecFile.Name),$codecFile.FullName,$null)
}
$ddf = @('.OPTION EXPLICIT','.Set CabinetNameTemplate=payload.cab',('.Set DiskDirectoryTemplate="' + $work + '"'),'.Set CompressionType=MSZIP','.Set Cabinet=ON','.Set Compress=ON','.Set MaxDiskSize=0','.Set MaxCabinetSize=0','.Set MaxDiskFileCount=0','.Set CabinetFileCountThreshold=0','.Set FolderFileCountThreshold=0','.Set FolderSizeThreshold=0')
$sequence = 0
foreach ($file in $payload) {
    $sequence++
    $item = Get-Item -LiteralPath $file[3]
    Add-Row File @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @($file[0],$file[1],$file[2],[int]$item.Length,$file[4],16384,$sequence)
    $ddf += '"' + $item.FullName + '" ' + $file[0]
}
$ddfPath = Join-Path $work 'payload.ddf'; $ddf | Set-Content -LiteralPath $ddfPath -Encoding ASCII
Push-Location $work
try {
    & makecab.exe /V0 /F $ddfPath | Out-File -LiteralPath (Join-Path $work 'makecab.log')
    if ($LASTEXITCODE -ne 0) { throw ('Cabinet creation failed. See ' + (Join-Path $work 'makecab.log')) }
} finally { Pop-Location }
# All payload files must fit in the one cabinet embedded below. MakeCAB's
# default disk limits otherwise split large runtime files into multiple cabinets.
$cabStream = [IO.File]::OpenRead((Join-Path $work 'payload.cab'))
try {
    $header = New-Object byte[] 36
    if ($cabStream.Read($header,0,36) -ne 36 -or [Text.Encoding]::ASCII.GetString($header,0,4) -ne 'MSCF' -or [BitConverter]::ToUInt16($header,28) -ne $sequence -or ([BitConverter]::ToUInt16($header,30) -band 3) -ne 0 -or [BitConverter]::ToUInt32($header,8) -ne $cabStream.Length) { throw 'Installer cabinet is incomplete or split across media.' }
} finally { $cabStream.Dispose() }
Add-Row Media @('DiskId','LastSequence','Cabinet') @(1,$sequence,'#payload.cab')
Add-Row _Streams @('Name','Data') @('payload.cab',(Join-Path $work 'payload.cab')) 1
$registryRows = @(
    @('InstallMarker',1,'Software\QuietPhotos','InstallDirectory','[INSTALLDIR]','AppComponent'),
    @('DocsMarker',1,'Software\QuietPhotos','DocumentationVersion','1.1.1','DocsComponent'),
    @('CodecMarker',1,'Software\QuietPhotos','CodecVersion','14.17.2','CodecComponent'),
    @('DesktopMarker',1,'Software\QuietPhotos','DesktopShortcut','#1','DesktopComponent'),
    @('FriendlyName',1,'Software\Classes\Applications\QuietPhotos.exe','FriendlyAppName','Quiet Photos','AppComponent'),
    @('OpenCommand',1,'Software\Classes\Applications\QuietPhotos.exe\shell\open\command',$null,'"[#AppExe]" "%1"','AppComponent'),
    @('DefaultIcon',1,'Software\Classes\Applications\QuietPhotos.exe\DefaultIcon',$null,'[#AppExe],0','AppComponent')
)
foreach ($row in $registryRows) { Add-Row Registry @('Registry','Root','Key','Name','Value','Component_') $row }
$extensionsSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\ImageLoader.cs') -Raw
$extensionDeclaration = [regex]::Match($extensionsSource,'Extensions = new HashSet<string>\(new\[\] \{([^}]+)\}').Groups[1].Value
$exts = @([regex]::Matches($extensionDeclaration,'"(\.[a-z0-9]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($exts.Count -lt 20) { throw 'Could not read the supported-format list for installer registration.' }
for ($i=0; $i -lt $exts.Count; $i++) { Add-Row Registry @('Registry','Root','Key','Name','Value','Component_') @(('Type' + $i),1,'Software\Classes\Applications\QuietPhotos.exe\SupportedTypes',$exts[$i],'','AppComponent') }
Add-Row Icon @('Name','Data') @('QuietPhotosIcon',(Join-Path $projectRoot 'assets\icons\QuietPhotos.ico')) 1
Add-Row Binary @('Name','Data') @('IconImage',(Join-Path $projectRoot 'assets\icons\QuietPhotos.ico')) 1
Add-Row Binary @('Name','Data') @('PhotosChoiceExe',(Join-Path $work 'PhotosChoice.exe')) 1
# UI-only, after a successful ExecuteAction. Ignore helper failure so optional Photos removal cannot undo Quiet Photos.
Add-Row CustomAction @('Action','Type','Source','Target') @('OfferPhotosRemoval',66,'PhotosChoiceExe','')
foreach ($row in @(@('StartMenuShortcut','AppMenuDir','AppComponent'),@('DesktopShortcut','DesktopFolder','DesktopComponent'))) {
    Add-Row Shortcut @('Shortcut','Directory_','Name','Component_','Target','Description','Icon_','IconIndex','ShowCmd','WkDir') @($row[0],$row[1],'QPHOTO~1|Quiet Photos',$row[2],'[#AppExe]','View local images','QuietPhotosIcon',0,1,'INSTALLDIR')
}
foreach ($row in @(@('RemoveMenu','AppComponent','AppMenuDir'),@('RemoveDocs','DocsComponent','DOCSDIR'),@('RemoveCodecs','CodecComponent','CODECDIR'),@('RemoveAppDir','AppComponent','INSTALLDIR'))) { Add-Row RemoveFile @('FileKey','Component_','DirProperty','InstallMode') @($row[0],$row[1],$row[2],2) }
Add-Row RegLocator @('Signature_','Root','Key','Name','Type') @('Net48',2,'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full','Release',18)
Add-Row AppSearch @('Property','Signature_') @('NETFRAMEWORK48','Net48')
Add-Row Signature @('Signature','FileName') @('ReservedFileSignature','QuietPhotos.exe')
Add-Row CustomAction @('Action','Type','Source','Target') @('SetMenuRoot',51,'ProgramMenuFolder','[QPMENUROOT]')
Add-Row CustomAction @('Action','Type','Source','Target') @('SetDesktopRoot',51,'DesktopFolder','[QPDESKTOPROOT]')
Add-Row LaunchCondition @('Condition','Description') @('Installed OR VersionNT64','Quiet Photos requires 64-bit Windows 10 or later.')
Add-Row LaunchCondition @('Condition','Description') @('Installed OR (NETFRAMEWORK48 >= "#528040")','Quiet Photos requires .NET Framework 4.8. Install it through Windows before continuing.')
Add-Row LaunchCondition @('Condition','Description') @('Installed OR NOT NEWERPRODUCTS','A newer version of Quiet Photos is already installed.')
Add-Row Upgrade @('UpgradeCode','VersionMin','VersionMax','Attributes','ActionProperty') @($upgradeCode,'0.0.0','1.1.1',257,'OLDPRODUCTS')
Add-Row Upgrade @('UpgradeCode','VersionMin','Attributes','ActionProperty') @($upgradeCode,'1.1.1',2,'NEWERPRODUCTS')
$execute = [ordered]@{ FindRelatedProducts=25; AppSearch=50; LaunchConditions=100; ValidateProductID=700; CostInitialize=800; FileCost=900; CostFinalize=1000; InstallValidate=1400; InstallInitialize=1500; RemoveExistingProducts=1600; ProcessComponents=1700; UnpublishFeatures=1800; RemoveRegistryValues=2600; RemoveShortcuts=3200; RemoveFiles=3500; RemoveFolders=3600; CreateFolders=3700; InstallFiles=4000; CreateShortcuts=4500; WriteRegistryValues=5000; RegisterUser=6000; RegisterProduct=6100; PublishFeatures=6300; PublishProduct=6400; InstallFinalize=6600 }
foreach ($entry in $execute.GetEnumerator()) { Add-Row InstallExecuteSequence @('Action','Sequence') @($entry.Key,[int]$entry.Value) }
foreach ($table in @('InstallExecuteSequence','InstallUISequence')) {
    Add-Row $table @('Action','Condition','Sequence') @('SetMenuRoot','QPMENUROOT',810)
    Add-Row $table @('Action','Condition','Sequence') @('SetDesktopRoot','QPDESKTOPROOT',820)
}
foreach ($row in @(@('FindRelatedProducts',25),@('AppSearch',50),@('LaunchConditions',100),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('ExecuteAction',1300))) { Add-Row InstallUISequence @('Action','Sequence') $row }
foreach ($row in @(@('WelcomeDlg','NOT Installed',1200),@('MaintenanceDlg','Installed AND NOT REMOVE',1201),@('ProgressDlg', $null,1290),@('ExitDlg',$null,-1),@('FailureDlg',$null,-3))) { Add-Row InstallUISequence @('Action','Condition','Sequence') $row }
Add-Row InstallUISequence @('Action','Condition','Sequence') @('OfferPhotosRemoval','NOT Installed AND NOT REMOVE AND NOT REINSTALL AND UILevel >= 5',1350)
Add-Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('Body','Segoe UI',9,0)
Add-Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('Heading','Segoe UI',18,1)
Add-Row CheckBox @('Property','Value') @('DESKTOP_SHORTCUT','1')
function Add-Dialog([string]$Name,[int]$Attributes,[string]$First,[string]$Default,[string]$Cancel) { Add-Row Dialog @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @($Name,50,50,370,245,$Attributes,'Quiet Photos Setup',$First,$Default,$Cancel) }
function Add-Control([string]$Dialog,[string]$Name,[string]$Type,[int]$X,[int]$Y,[int]$W,[int]$H,[string]$Text,[string]$Property=$null,[string]$Next=$null,[int]$Attributes=3) {
    if ($Type -eq 'Icon') { $Attributes = 6291459 }
    Add-Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') @($Dialog,$Name,$Type,$X,$Y,$W,$H,$Attributes,$Property,$Text,$Next)
}
function Add-Event([string]$Dialog,[string]$Control,[string]$Event,[string]$Argument,[int]$Order=1) { Add-Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @($Dialog,$Control,$Event,$Argument,'1',$Order) }
Add-Dialog WelcomeDlg 3 Install Install Cancel
Add-Control WelcomeDlg Icon Icon 20 25 44 44 IconImage
Add-Control WelcomeDlg Heading Text 78 26 275 28 '{\Heading}Quiet Photos'
Add-Control WelcomeDlg Body Text 78 65 264 45 'Installs for your account. After installation, setup checks for Microsoft Photos and offers optional removal.'
Add-Control WelcomeDlg Location Text 25 119 320 32 'Install location: [INSTALLDIR]'
Add-Control WelcomeDlg Desktop CheckBox 25 162 320 18 'Create a desktop shortcut' DESKTOP_SHORTCUT Install
Add-Control WelcomeDlg Install PushButton 198 210 76 22 'Install' $null Cancel
Add-Control WelcomeDlg Cancel PushButton 281 210 64 22 'Cancel' $null Desktop
Add-Event WelcomeDlg Install EndDialog Return
Add-Event WelcomeDlg Cancel EndDialog Exit
Add-Dialog MaintenanceDlg 3 Repair Repair Cancel
Add-Control MaintenanceDlg Icon Icon 20 25 44 44 IconImage
Add-Control MaintenanceDlg Heading Text 78 26 275 28 '{\Heading}Quiet Photos'
Add-Control MaintenanceDlg Body Text 25 91 320 65 'Quiet Photos is already installed. Repair restores its installed files and shortcuts. Remove uninstalls the application.'
Add-Control MaintenanceDlg Repair PushButton 110 210 74 22 'Repair' $null Remove
Add-Control MaintenanceDlg Remove PushButton 190 210 74 22 'Remove' $null Cancel
Add-Control MaintenanceDlg Cancel PushButton 270 210 74 22 'Cancel' $null Repair
Add-Event MaintenanceDlg Repair '[REINSTALL]' ALL 1
Add-Event MaintenanceDlg Repair '[REINSTALLMODE]' omus 2
Add-Event MaintenanceDlg Repair EndDialog Return 3
Add-Event MaintenanceDlg Remove '[REMOVE]' ALL 1
Add-Event MaintenanceDlg Remove EndDialog Return 2
Add-Event MaintenanceDlg Cancel EndDialog Exit
Add-Dialog ProgressDlg 1 $null $null $null
Add-Control ProgressDlg Heading Text 25 30 320 30 '{\Heading}Working on it…'
Add-Control ProgressDlg Body Text 25 90 320 40 'Please wait while Windows installs or removes Quiet Photos.'
Add-Control ProgressDlg Progress ProgressBar 25 155 320 14 ''
Add-Control ProgressDlg Action Text 25 181 320 28 ''
Add-Row EventMapping @('Dialog_','Control_','Event','Attribute') @('ProgressDlg','Progress','SetProgress','Progress')
Add-Row EventMapping @('Dialog_','Control_','Event','Attribute') @('ProgressDlg','Action','ActionText','Text')
foreach ($name in @('ExitDlg','FailureDlg')) {
    Add-Dialog $name 3 Finish Finish Finish
    Add-Control $name Icon Icon 20 25 44 44 IconImage
    $heading = if ($name -eq 'ExitDlg') { '{\Heading}Setup complete' } else { '{\Heading}Setup could not finish' }
    $body = if ($name -eq 'ExitDlg') { 'Windows has finished updating Quiet Photos. If installed, you can open it from the Start menu.' } else { 'Windows could not complete setup. No further action will be taken by this installer.' }
    Add-Control $name Heading Text 78 26 275 48 $heading
    Add-Control $name Body Text 25 97 320 65 $body
    Add-Control $name Finish PushButton 269 210 76 22 'Finish' $null Finish
    Add-Event $name Finish EndDialog Return
}
Add-Dialog ErrorDialog 65539 ErrorOK ErrorOK ErrorOK
Add-Control ErrorDialog ErrorText Text 25 35 320 140 '[ErrorMessage]'
Add-Control ErrorDialog ErrorOK PushButton 269 210 76 22 'OK' $null ErrorOK
Add-Event ErrorDialog ErrorOK EndDialog Error
foreach ($row in @(@('InstallFiles','Copying application files'),@('CreateShortcuts','Creating shortcuts'),@('WriteRegistryValues','Registering application'),@('RemoveFiles','Removing installed files'),@('RemoveShortcuts','Removing shortcuts'))) { Add-Row ActionText @('Action','Description') $row }
$summary = $database.SummaryInformation(20)
foreach ($entry in @(@(1,1252),@(2,'Installation Database'),@(3,'Quiet Photos 1.1.1'),@(4,'Quiet Photos'),@(5,'Installer'),@(7,'x64;1033'),@(9,('{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}')),@(14,500),@(15,10),@(18,'Quiet Photos build tools'),@(19,2))) {
    [void]$summary.GetType().InvokeMember('Property',[Reflection.BindingFlags]::SetProperty,$null,$summary,@($entry[0],$entry[1]))
}
$summary.Persist(); $database.Commit()
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($summary)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
Write-Host ('Built ' + $msiPath)
