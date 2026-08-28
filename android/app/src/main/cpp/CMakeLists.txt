cmake_minimum_required(VERSION 3.22.1)

project("fimagestack_native")

add_library(
    fimagestack_native
    SHARED
    fstack_jni.cpp
)

find_library(log-lib log)
find_library(jnigraphics-lib jnigraphics)

target_compile_options(fimagestack_native PRIVATE -O3 -std=c++17 -fopenmp)
target_link_options(fimagestack_native PRIVATE -static-openmp -fopenmp)

target_link_libraries(
    fimagestack_native
    ${log-lib}
    ${jnigraphics-lib}
)
