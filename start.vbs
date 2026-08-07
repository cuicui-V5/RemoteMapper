' RemoteMic background launcher — starts RemoteMic.exe with no visible window.
' Double-click this to run RemoteMic in the background. Logs go to RemoteMic.log.
' Stop with stop.bat (or taskkill /F /IM RemoteMic.exe).

Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)

' Already running? don't start a second instance (only one can own the remote).
If fso.FileExists(here & "\RemoteMic.exe") Then
    Set wmi = GetObject("winmgmts:\\.\root\cimv2")
    Set procs = wmi.ExecQuery("SELECT * FROM Win32_Process WHERE Name='RemoteMic.exe'")
    If procs.Count > 0 Then
        MsgBox "RemoteMic is already running in the background." & vbCrLf & _
               "Use stop.bat to stop it first.", vbInformation, "RemoteMic"
        WScript.Quit
    End If
Else
    MsgBox "RemoteMic.exe not found next to this script:" & vbCrLf & here, vbCritical, "RemoteMic"
    WScript.Quit
End If

' Launch hidden (0 = hidden window, False = don't wait).
sh.CurrentDirectory = here
sh.Run "RemoteMic.exe", 0, False