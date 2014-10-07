# **********************************************************************
#
# Copyright (c) 2003-2014 ZeroC, Inc. All rights reserved.
#
# This copy of Ice is licensed to you under the terms described in the
# ICE_LICENSE file included in this distribution.
#
# **********************************************************************

top_srcdir	= ..\..\..

CLIENT		= client.exe
SERVER		= server.exe

TARGETS		= $(CLIENT) $(SERVER)

SLICE_OBJS	= .\Item.obj \
		  .\ItemInfo.obj

COBJS		= .\Item.obj \
		  .\Client.obj

SOBJS		= $(SLICE_OBJS) \
		  .\CurrentDatabase.obj \
		  .\Evictor.obj \
		  .\EvictorBase.obj \
		  .\ItemI.obj \
		  .\Server.obj \
		  .\SimpleEvictor.obj \
		  .\Database.obj

OBJS		= $(COBJS) \
		  $(SOBJS)

all:: Database.cpp Database.h

!include $(top_srcdir)/config/Make.rules.mak

CPPFLAGS	= -I. $(CPPFLAGS) -DWIN32_LEAN_AND_MEAN

!if "$(GENERATE_PDB)" == "yes"
CPDBFLAGS        = /pdb:$(CLIENT:.exe=.pdb)
SPDBFLAGS        = /pdb:$(SERVER:.exe=.pdb)
COPDBFLAGS       = /pdb:$(COLLOCATED:.exe=.pdb)
!endif

$(CLIENT): $(COBJS)
	$(LINK) $(LD_EXEFLAGS) $(CPDBFLAGS) $(SETARGV) $(COBJS) $(PREOUT)$@ $(PRELIBS)$(LIBS)
	@if exist $@.manifest echo ^ ^ ^ Embedding manifest using $(MT) && \
		$(MT) -nologo -manifest $@.manifest -outputresource:$@;#1 && del /q $@.manifest

$(SERVER): $(SOBJS)
	$(LINK) $(LD_EXEFLAGS) $(SPDBFLAGS) $(SETARGV) $(SOBJS) $(PREOUT)$@ $(PRELIBS)$(LIBS) 
	@if exist $@.manifest echo ^ ^ ^ Embedding manifest using $(MT) && \
		$(MT) -nologo -manifest $@.manifest -outputresource:$@;#1 && del /q $@.manifest

Database.h Database.cpp: ItemInfo.ice "$(SLICE2FREEZE)" "$(SLICEPARSERLIB)"
	del /q Database.h Database.cpp
	"$(SLICE2FREEZE)" -I. -I"$(slicedir)" --dict Database,string,Warehouse::ItemInfo Database ItemInfo.ice

clean::
	del /q Item.h Item.cpp
	del /q ItemInfo.h ItemInfo.cpp
	del /q Database.h Database.cpp
	if exist db\__Freeze rmdir /q /s db\__Freeze
	for %f in (db\*) do if not %f == db\DB_CONFIG del /q %f
