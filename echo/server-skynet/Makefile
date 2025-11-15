LUA_DIR = skynet/3rd/lua
CJSON_DIR = 3rd/cjson

all:
	gcc -O3 -fPIC \
	-I$(LUA_DIR) \
	-I$(LUA_DIR)/jit \
	$(CJSON_DIR)/lua_cjson.c \
	$(CJSON_DIR)/strbuf.c \
	$(CJSON_DIR)/fpconv.c \
	-shared -o luaclib/cjson.so