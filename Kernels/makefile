# Use toolchain from: https://github.com/haarer/toolchain68k
# Configure for m68k-elf target and install to default location
# Tested on EL6 and Fedora 28
#
pcm ?= P01
address ?= FF8000

PREFIX = /opt/crosschain/bin/m68k-elf-

CC = $(PREFIX)gcc
LD = $(PREFIX)ld
OBJCOPY = $(PREFIX)objcopy
OBJDUMP = $(PREFIX)objdump

BUILD_EPOCH := $(shell date +%s)
SRC_DIR = 68k-VPW-C
BUILD_DIR = build
CCFLAGS = -c -fomit-frame-pointer -std=gnu99 -mcpu=68332 -D$(pcm) -DBUILD_EPOCH=$(BUILD_EPOCH)
LDFLAGS =
DUMPFLAGS = -d -S
COPYFLAGS = -O binary

# PCM specific C files list
FILE_LIST = $(SRC_DIR)/CFiles-$(pcm).list
_CFILES_BARE = $(shell col -b < ${FILE_LIST})
_CFILES = $(addprefix $(SRC_DIR)/,$(_CFILES_BARE))

# PCM specific object file list
OFILES = $(_CFILES:.c=.o)

# PCM specific Linker Script (.ld)
PCM_LDSCRIPT = SECTIONS { .text \(0x12340000\) : { $(SRC_DIR)/main.o } .kernel_code :	{ $(SRC_DIR)/Kernel-$(pcm).o \(.kernelstart\) \* \(.text\) } .kernel_data : { \* \(.kerneldata\) }}

all: $(BUILD_DIR)/Kernel-$(pcm).bin

$(SRC_DIR)/%.o: $(SRC_DIR)/%.c
	$(CC) $(CCFLAGS) $< -o $@

$(BUILD_DIR)/Kernel-$(pcm).elf: $(SRC_DIR)/Kernel-$(pcm).o $(OFILES)
	@mkdir -p $(BUILD_DIR)
	@echo $(PCM_LDSCRIPT) > $(BUILD_DIR)/LinkerScript.tmp
	$(LD) $(LDFLAGS) --section-start .kernel_code=0x$(address) -T $(BUILD_DIR)/LinkerScript.tmp -o $@ $(SRC_DIR)/Kernel-$(pcm).o $(OFILES)
	-@rm -f $(BUILD_DIR)/LinkerScript.tmp
	-$(OBJDUMP) $(DUMPFLAGS) $@ > $(BUILD_DIR)/Kernel-$(pcm).disassembly

$(BUILD_DIR)/Kernel-$(pcm).bin: $(BUILD_DIR)/Kernel-$(pcm).elf
	$(OBJCOPY) $(COPYFLAGS) --only-section=.kernel_code --only-section=.rodata $< $@

clean:
	@rm -f $(SRC_DIR)/*.o $(BUILD_DIR)/*.bin $(BUILD_DIR)/*.elf $(BUILD_DIR)/*.disassembly $(BUILD_DIR)/*.tmp
