PS4_HOST ?= ps4
PS4_PORT ?= 9090

ifndef PS4_PAYLOAD_SDK
$(error PS4_PAYLOAD_SDK is undefined)
endif

include $(PS4_PAYLOAD_SDK)/toolchain/orbis.mk

ELF := template.elf
OBJ := main.o jailbreak.o kernelrw.o

CFLAGS := -Wall -Wextra -Werror -O2 -g
JBC_CFLAGS := -Wall -Wextra -O2 -g 	-Wno-unused-variable -Wno-unused-parameter -Wno-sign-compare

all: $(ELF)

$(ELF): $(OBJ)
	$(CC) -o $@ $(OBJ)

main.o: main.c ps4-libjbc/jailbreak.h
	$(CC) $(CFLAGS) -c -o $@ main.c

jailbreak.o: ps4-libjbc/jailbreak.c ps4-libjbc/jailbreak.h ps4-libjbc/kernelrw.h ps4-libjbc/defs.h
	$(CC) $(JBC_CFLAGS) -c -o $@ ps4-libjbc/jailbreak.c

kernelrw.o: ps4-libjbc/kernelrw.c ps4-libjbc/kernelrw.h ps4-libjbc/defs.h
	$(CC) $(JBC_CFLAGS) -c -o $@ ps4-libjbc/kernelrw.c

clean:
	rm -f $(ELF) $(OBJ)

test: $(ELF)
	$(PS4_DEPLOY) -h $(PS4_HOST) -p $(PS4_PORT) $^
