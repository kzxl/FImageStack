cmake_minimum_required(VERSION 3.22.1)

project("fimagestack_native")

add_library(
    fimagestack_native
    SHARED
    fstack_jni.cpp
)

find_library(
    log-lib
    log
)

find_library(
    jnigraphics-lib
    jnigraphics
)

target_link_libraries(
    fimagestack_native
    ${log-lib}
    ${jnigraphics-lib}
)
