PS4_HOST ?= ps4
PS4_PORT ?= 9090

ifndef PS4_PAYLOAD_SDK
$(error PS4_PAYLOAD_SDK is undefined)
endif

include $(PS4_PAYLOAD_SDK)/toolchain/orbis.mk

ELF := template.elf
SRC := main.c ps4-libjbc/jailbreak.c ps4-libjbc/kernelrw.c
CFLAGS := -Wall -Wextra -Werror -O2 -g

all: $(ELF)

$(ELF): $(SRC)
	$(CC) $(CFLAGS) -o $@ $(SRC)

clean:
	rm -f $(ELF)

test: $(ELF)
	$(PS4_DEPLOY) -h $(PS4_HOST) -p $(PS4_PORT) $^
