
all: Loom.exe
	
Loom.exe: Loom/*.cs
	mcs -debug Loom/*.cs /out:$@
