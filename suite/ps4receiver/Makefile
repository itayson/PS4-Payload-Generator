TITLE       := Tayson PS4 Receiver
VERSION     := 1.42
TITLE_ID    := TYRC00001
CONTENT_ID  := IV0000-TYRC00001_00-TAYSONRECEIVER00

LIBS        := -lc -lkernel -lSceNet -lSceSsl -lSceHttp -lSceSysmodule 
EXTRAFLAGS  := -O2 -Wall -Wextra

TOOLCHAIN   := $(OO_PS4_TOOLCHAIN)
PROJDIR     := receiver
INTDIR      := build
CFILES      := $(wildcard $(PROJDIR)/*.c)
OBJS        := $(patsubst $(PROJDIR)/%.c,$(INTDIR)/%.o,$(CFILES))
JBC_OBJ     := third_party/jbc.o

CFLAGS      := --target=x86_64-pc-freebsd12-elf -fPIC -funwind-tables -c $(EXTRAFLAGS) -isysroot $(TOOLCHAIN) -isystem $(TOOLCHAIN)/include
LDFLAGS     := -m elf_x86_64 -pie --script $(TOOLCHAIN)/link.x --eh-frame-hdr -L$(TOOLCHAIN)/lib $(LIBS) $(TOOLCHAIN)/lib/crt1.o

UNAME_S     := $(shell uname -s)
ifeq ($(UNAME_S),Linux)
CC          := clang
LD          := ld.lld
CDIR        := linux
endif
ifeq ($(UNAME_S),Darwin)
CC          := /usr/local/opt/llvm/bin/clang
LD          := /usr/local/opt/llvm/bin/ld.lld
CDIR        := macos
endif

all: $(CONTENT_ID).pkg

$(INTDIR):
	mkdir -p $(INTDIR) sce_sys/about

$(INTDIR)/%.o: $(PROJDIR)/%.c | $(INTDIR)
	$(CC) $(CFLAGS) -o $@ $<

$(INTDIR)/receiver.elf: $(OBJS) $(JBC_OBJ)
	$(LD) $(OBJS) $(JBC_OBJ) -o $@ $(LDFLAGS)

eboot.bin: $(INTDIR)/receiver.elf
	$(TOOLCHAIN)/bin/$(CDIR)/create-fself -in=$< -out=$(INTDIR)/receiver.oelf --eboot "eboot.bin" --paid 0x3800000000000011

sce_sys/icon0.png: make_icon.py
	python3 make_icon.py $@

sce_sys/param.sfo: Makefile
	mkdir -p sce_sys
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_new $@
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ APP_TYPE --type Integer --maxsize 4 --value 1
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ APP_VER --type Utf8 --maxsize 8 --value '$(VERSION)'
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ ATTRIBUTE --type Integer --maxsize 4 --value 0
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ CATEGORY --type Utf8 --maxsize 4 --value 'gd'
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ CONTENT_ID --type Utf8 --maxsize 48 --value '$(CONTENT_ID)'
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ DOWNLOAD_DATA_SIZE --type Integer --maxsize 4 --value 0
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ SYSTEM_VER --type Integer --maxsize 4 --value 0
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ TITLE --type Utf8 --maxsize 128 --value '$(TITLE)'
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ TITLE_ID --type Utf8 --maxsize 12 --value '$(TITLE_ID)'
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core sfo_setentry $@ VERSION --type Utf8 --maxsize 8 --value '$(VERSION)'

RUNTIME_MODULES := $(wildcard sce_module/*.prx)
EXTRA_PACKAGE_FILES := $(wildcard sce_sys/about/*.sprx)

pkg.gp4: eboot.bin sce_sys/param.sfo sce_sys/icon0.png $(RUNTIME_MODULES) $(EXTRA_PACKAGE_FILES)
	$(TOOLCHAIN)/bin/$(CDIR)/create-gp4 -out $@ --content-id=$(CONTENT_ID) --files "$^"

$(CONTENT_ID).pkg: pkg.gp4
	$(TOOLCHAIN)/bin/$(CDIR)/PkgTool.Core pkg_build $< .

clean:
	rm -rf $(INTDIR) eboot.bin pkg.gp4 $(CONTENT_ID).pkg sce_sys/param.sfo sce_sys/icon0.png
