# Chattha Sangayana Tipitaka (CST)

This is the repository for CST4 (Windows) as well as development for future cross-platform clients.

The main branch has version 4.2, a work in progress to convert to the use of Lucene.NET 4.8. This is the groundwork for an upgrade to more recent cross-platform .NET releases and clients for non-Windows platforms.

The commits corresponding to the currently released versions of 4.0 and 4.1 are on separate branches and the most recent releases are also tagged.

## Build Instructions
Install Microsoft Visual Studio 2022 or later

Install [Wix Toolset v3](https://wixtoolset.org/docs/wix3/). Note that this version will not be supported after Feb. 2025 and the installer process should be updated to use WiX v5.

Add the WiX binaries directory to your path. For me as of this writing that path is
C:\Program Files (x86)\WiX Toolset v3.14\bin

Make sure you have Windows PowerShell installed. Open a new PowerShell window and run "candle" and "light", the two WiX binaries, to ensure that they are in your path. 

Pull the cst repo

Pull the deva directory of the tipitaka-xml repo ( https://github.com/VipassanaTech/tipitaka-xml/tree/main/deva )
The deva directory should be at the same level as the cst repo directory. One way to do that is with a git sparse checkout. Here are the commands to do that.

cd to the parent of the cst repo.
<TODO>


Switch to the latest branch: cst_4_1

Open the solution at src/CST.sln in Visual Studio 2022 or later.

Build the solution (Build menu -> Build Solution)

<TODO>




