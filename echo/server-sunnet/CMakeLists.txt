cmake_minimum_required(VERSION 3.25.0)
project(server-sunnet)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)
include_directories(sunnet/include)
aux_source_directory(./src DIR_SRCS)
aux_source_directory(./sunnet/src SUNNET_SRCS)
find_package(Threads)
add_executable(server-sunnet ${DIR_SRCS} ${SUNNET_SRCS})
target_link_libraries(server-sunnet ${CMAKE_THREAD_LIBS_INIT} pthread)
