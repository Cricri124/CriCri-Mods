cmake_minimum_required(VERSION 3.22.1)
project(unityLibrary CXX)

# CriCri Mods (08/11/2026): the APK goes to the public, so the builder's folders (source tree, build tree, NDK) never go
# into the libraries: __FILE__ and the debug info get neutral prefixes instead of the absolute paths of this PC.
add_compile_options(
    "-ffile-prefix-map=${CMAKE_SOURCE_DIR}=src"
    "-ffile-prefix-map=${CMAKE_BINARY_DIR}=build"
    "-ffile-prefix-map=${ANDROID_NDK}=ndk")

add_subdirectory(main)
add_subdirectory(fusion)
