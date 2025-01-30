This project will communicate with a Qardio blood pressure monitor over Bluetooth Low Energy (BTLE).

You need Visual Studio (e.g. Community Edition) to build and use this app.

If you are not interested in Apple Health sync (which should sync with e.g. LoseIt on iOS) then no need to do the following (instead, use your own BP tracking software manually to enter the readings):

Install-Package Dropbox.Api

Goto https://www.dropbox.com/developers
    Create apps
    Scoped access
    App folder
    App name: BpMon
    Permissions: files.metadata.write, files.content.write
    Submit

Then compile and run the app. It will launch a web page to authorize the app with Dropbox and give you an authorization code. Copy that authorization code into Dropbox\DropboxUploader.cs\m_authorizationCode and rebuild.
Once you click on the Start button and take a blood pressure reading, you should now see in your Dropbox account an Apps/BpMon/bp.csv file containing the blood pressure reading which iOS Apple Health can import with a Shortcut.

To create the Apple Health Shortcut on iOS:

    Shortcuts
    Rename to BP
    Get Dropbox File
    Unselect Show Documet Picker
    File Path: /Apps/BpMon/bp.csv.timestamp
    If Fize Size does not have any value
        Get Dropbox File
        Unselect Show Documet Picker
        File Path: /Apps/BpMon/bp.csv
        Scripting -> split file by newlines
        Repeat with each item in Split Text
            Split Repeat item by Custom ,
            Get First Item from Split Text (drag over Get Item From List and select variable Split Text)
            If DateField is Date (here tap on Item from list and rename to DateField)
                Otherwise
                Get Dates from Input -> Get Dates from DateField
                Get Item at Index 2 from Split Text
                Get Last Item from Split Text
                Log Health Sample
                    Blood Pressure
                    SystolicField (tap on Item from list for the Item at Index 2 from Split Text output and rename to SystolicField and change type to Number)
                    DiastolicField (rename Item from list output from Last Item from Split Text to DiastolicField and change type to Number)
                    Dates
            End If
        End Repeat
        Save Dropbox File
            /Apps/BpMon/bp.csv.timestamp
            Set variable to current date
    End If

Then just create an automation to run the BP shortcut at 12am each day.

The shortcut will save a timestamp file which lets the shortcut know not to run more than once for the same CSV data. The app then deletes the timestamp file when it takes a new blood pressure measurement
and creates a new CSV, allowing the shortcut to import the new data from bp.csv (this handshake was needed since Dropbox shortcut options no longer allow deleting files).
