ifndef PS4_PAYLOAD_SDK
$(error Set PS4_PAYLOAD_SDK to ps4-payload-sdk v0.9)
endif
include $(PS4_PAYLOAD_SDK)/toolchain/orbis.mk

all: tayson_probe.elf

tayson_probe.elf: main.c job_wire.h
	$(CC) -O2 -Wall -Wextra -Werror -mstackrealign main.c -o $@ -lpthread

clean:
	rm -f tayson_probe.elf
