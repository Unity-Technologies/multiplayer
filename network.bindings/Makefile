CURRENT_DIR					:= $(shell pwd)
BIN_DIR						:= bin
BUILD_DIR					:= build
NETWORK_BINDINGS_DIR		:= network.bindings
NETWORK_BINDINGS_SRC_DIR	:= source
NETWORK_BINDINGS_OBJ_DIR	:= $(BUILD_DIR)/$(NETWORK_BINDINGS_DIR)/obj
UNITY_PROJECT_DIR			:= $(CURRENT_DIR)/../sampleproject
UNITY_PROJECT_BINDINGS_DIR	:= $(CURRENT_DIR)/../com.unity.transport/Runtime/Bindings

NATIVE_CC					:= clang
NATIVE_FLAGS				:= -Wall -Wextra -std=gnu99 -g
NATIVE_FLAGSR				:= -Wall -Wextra -g
LINUX_NATIVE_FLAGS			:= -fPIC

MAC_LINKER_FLAGS			:= -bundle 
LINUX_LINKER_FLAGS			:= -shared -Wl,-soname,libnetwork.bindings.so
OS_TYPE						:= $(shell uname)

# =============================================================================

all: createdirs network.bindings

createdirs:
	@if [ ! -d $(BIN_DIR) ];then mkdir $(BIN_DIR);fi
	@if [ ! -d $(BUILD_DIR) ];then mkdir $(BUILD_DIR);fi
	@if [ ! -d $(NETWORK_BINDINGS_OBJ_DIR) ];then mkdir -p $(NETWORK_BINDINGS_OBJ_DIR);fi

# =============================================================================

NATIVE_HDRS				:= \
					$(NETWORK_BINDINGS_SRC_DIR)/network.bindings.h

# =============================================================================

NATIVE_OBJS				:= \
					$(NETWORK_BINDINGS_OBJ_DIR)/network.bindings.o


DRIVER_OBJS				:= \
					$(NETWORK_BINDINGS_OBJ_DIR)/network.bindings.o \
					$(NETWORK_BINDINGS_OBJ_DIR)/driver.o
# =============================================================================

$(NETWORK_BINDINGS_OBJ_DIR)/network.bindings.o: $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.c
ifeq ($(OS_TYPE),Linux)
	$(NATIVE_CC) $(LINUX_NATIVE_FLAGS) $(NATIVE_FLAGS) -o $@ -c $<
else
	$(NATIVE_CC) $(NATIVE_FLAGS) -o $@ -c $<
endif

$(NETWORK_BINDINGS_OBJ_DIR)/driver.o: $(NETWORK_BINDINGS_SRC_DIR)/driver/driver.cpp
	clang++ $(NATIVE_FLAGSR) -I$(CURRENT_DIR)/$(NETWORK_BINDINGS_SRC_DIR)/  -o $@ -c $<

# =============================================================================

network.bindings: $(NATIVE_OBJS)
ifeq ($(OS_TYPE),Linux)
	$(NATIVE_CC) $(NATIVE_FLAGS) $(LINUX_LINKER_FLAGS) $^ -o $(BIN_DIR)/lib$@.so
else
	$(NATIVE_CC) $(NATIVE_FLAGS) $(MAC_LINKER_FLAGS) $^ -o $(BIN_DIR)/$@.bundle 
endif

#gamesocket.managed:
#	mcs -t:library gamesocket.managed/multiplay_managed.cs -out:$(BIN_DIR)/gamesocket.managed.dll -sdk:2 -unsafe

driver: $(DRIVER_OBJS)
	clang++ $^ -o $(BIN_DIR)/$@

# Native library needs to be in a public place like ~/lib or /usr/lib or inside the unity project root
install:
	#./install_lib.sh libgamesocket.native.dylib
	mkdir -p $(UNITY_PROJECT_BINDINGS_DIR)
	cp $(BIN_DIR)/*network.bindings.* $(UNITY_PROJECT_BINDINGS_DIR)
	cp $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.cs $(UNITY_PROJECT_BINDINGS_DIR)
	cp $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.cs.meta $(UNITY_PROJECT_BINDINGS_DIR)
	cp $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.bundle.meta $(UNITY_PROJECT_BINDINGS_DIR)

	# On iOS just copy source files to the ios directory
	mkdir -p $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.c $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/network.bindings.h $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/platform.h $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/meta/ios.meta $(UNITY_PROJECT_BINDINGS_DIR)
	cp $(NETWORK_BINDINGS_SRC_DIR)/meta/network.bindings.c.meta $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/meta/network.bindings.h.meta $(UNITY_PROJECT_BINDINGS_DIR)/ios
	cp $(NETWORK_BINDINGS_SRC_DIR)/meta/platform.h.meta $(UNITY_PROJECT_BINDINGS_DIR)/ios

clean:
	@rm -rf $(BIN_DIR)
	@rm -rf $(BUILD_DIR)

.PHONY: clean createdirs libgamesocket.native.dylib gamesocket.managed
