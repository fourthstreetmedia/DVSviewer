# DVSviewer
Load and extract information from .dvs files created by DTI Technologies CCTV software.
<img width="986" height="713" alt="image" src="https://github.com/user-attachments/assets/8f13553c-ae4d-4a22-8223-2c892e832afe" />
<img width="986" height="713" alt="image" src="https://github.com/user-attachments/assets/57fbb3ec-1588-4a7f-93a2-df2a52884347" />
## Requirements
.NET Desktop Runtime version 10.0.11 or later - https://dotnet.microsoft.com/en-us/download/dotnet/10.0
## Compatibility
Should be compatible with all DTI Technologies .dvs files that would usually be played with DVSSPlayer. This program was tested using files provided through the San Francisco Municipal Transportation Agency (SFMTA/MUNI).
# Running the program (Prebuilt binaries)
### ALL VERSIONS
Download the latest .NET Desktop Runtime for your operating system: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 
### Windows
1. Download the latest release in [Releases](https://github.com/fourthstreetmedia/DVSviewer/releases) corresponding to your Windows version
2. Run the file, the programs GUI will show up upon open
### Linux/Mac
1. Download the latest release in [Releases](https://github.com/fourthstreetmedia/DVSviewer/releases) corresponding to your OS version
2. Extract the .zip and cd to the folder in your terminal (ex: cd ~/Downloads/linux-x64)
3. Run chmod +x ./{folder name} (ex: chmod +x ./linux-x64)
4. Run ./{folder name} in your terminal (ex: ./linux-x64)
5. Alternatively, instead of steps 3 and 4, you can cd another level in (ex: cd linux-x64) and run the command "dotnet DvsViewer.dll"
NOTE: Linux and OSX builds are untested, [create a GitHub issue](https://github.com/fourthstreetmedia/DVSviewer/issues) if it doesnt work.
## Source code
All source code is located in [/src/](https://github.com/fourthstreetmedia/DVSviewer/tree/main/src/)
## Licensing
This project does not have any association with DTI Group Ltd https://www.dti.com.au/, any DTI Group subsidiaries or the SFMTA https://www.sfmta.com/
<br>This project is licensed under the MIT Open Source License.
