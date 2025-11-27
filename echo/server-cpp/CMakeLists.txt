cmake_minimum_required(VERSION 3.16)
project(server-cpp)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(Threads REQUIRED)

add_executable(server-cpp
    main.cpp
    session.cpp
    protocol.cpp
)

target_link_libraries(server-cpp PRIVATE Threads::Threads)

