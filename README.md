# crunchersharp
Program analyses debugger information file (PDB, so Microsoft Visual C++ only) and presents info about user defined structures (size, padding, etc). 

Original blog post: http://msinilo.pl/blog/?p=425

Note that you will need `msdia90.dll` classes to be registered. Download VC++ 2008 redistributable (64 bit), install it, and also manually register the DLL (from admin command prompt: `regsvr32 msdia90.dll`).
