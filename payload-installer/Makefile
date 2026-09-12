MAKEFILE_DIR := $(dir $(abspath $(lastword $(MAKEFILE_LIST))))
PS4_PAYLOAD_SDK ?= $(MAKEFILE_DIR)ps4-payload-sdk
LLVM_CONFIG ?= $(shell command -v llvm-config-18 2>/dev/null || command -v llvm-config 2>/dev/null)

ifeq ($(strip $(LLVM_CONFIG)),)
$(error llvm-config is required; set LLVM_CONFIG to llvm-config-18)
endif

export LLVM_CONFIG
include $(PS4_PAYLOAD_SDK)/toolchain/orbis.mk

TARGET := Tayson-Payload-Installer-0.1.0
ELF := $(TARGET).elf
BIN := $(TARGET).bin
SRC := main.c

CFLAGS += -std=c11 -O2 -Wall -Wextra -Wno-unused-parameter -include compat.h

.PHONY: all clean
all: $(ELF) $(BIN)

$(ELF): $(SRC) compat.h
	$(CC) $(CFLAGS) -o $@ $<

$(BIN): $(ELF)
	cp $< $@
	$(STRIP) $@

clean:
	rm -f $(ELF) $(BIN)
