cmake_minimum_required(VERSION 3.16)
project(server-cpp)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(server-cpp
    main.cpp
    session.cpp
    protocol.cpp
    poller.cpp
    worker.cpp
)

