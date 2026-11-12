cmake_minimum_required(VERSION 3.21)
cmake_policy(SET CMP0091 NEW)

project(BSAudioDecode LANGUAGES C CXX)

# Targets
#   BSAudioDecode   shared  src/core + src/win (WIN32) | src/android (ANDROID)
#                           -> BSAudioDecode.dll / libBSAudioDecode.so
#                           exports: src/dll/BSAudioDecode.def (Windows), src/dll/bsad.map (Android)
#   bsadtool        exe     tools/bsadtool + the same sources: drives the C API as the C# side does
#                           (on the PC, or over adb shell on a device)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

if(MSVC)
    # Static CRT: no VC++ redistributable dependency in the player.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_library(bsad_common INTERFACE)
target_include_directories(bsad_common INTERFACE
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/src")
if(MSVC)
    target_compile_definitions(bsad_common INTERFACE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0A00)
    target_compile_options(bsad_common INTERFACE /W4 /WX /permissive- /utf-8 /Zc:__cplusplus /EHsc /guard:cf /sdl)
    target_link_options(bsad_common INTERFACE /guard:cf /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA $<$<CONFIG:Release>:/OPT:REF /OPT:ICF>)
elseif(ANDROID)
    target_compile_options(bsad_common INTERFACE
        -Wall -Wextra -Werror -Wno-unused-parameter -fstack-protector-strong
        -D__ANDROID_UNAVAILABLE_SYMBOLS_ARE_WEAK__ -Werror=unguarded-availability)
else()
    target_compile_options(bsad_common INTERFACE -Wall -Wextra -Wno-unused-parameter)
endif()

file(GLOB BSAD_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/core/*.cpp")
if(WIN32)
    file(GLOB BSAD_PLATFORM_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/win/*.cpp")
elseif(ANDROID)
    file(GLOB BSAD_PLATFORM_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/android/*.cpp")
else()
    message(FATAL_ERROR "BSAudioDecode builds for Windows and Android only")
endif()

set(BSAD_ALL_SOURCES ${BSAD_SOURCES} ${BSAD_PLATFORM_SOURCES})
if(WIN32)
    # Media Foundation is late-bound (LoadLibraryExW + GetProcAddress) so the DLL still loads on
    # Windows N editions; mfuuid is GUID data only.
    set(BSAD_PLATFORM_LIBRARIES ole32 mfuuid)
elseif(ANDROID)
    set(BSAD_PLATFORM_LIBRARIES mediandk log)
endif()

add_library(BSAudioDecode SHARED ${BSAD_ALL_SOURCES})
target_link_libraries(BSAudioDecode PRIVATE bsad_common ${BSAD_PLATFORM_LIBRARIES})
if(MSVC)
    target_sources(BSAudioDecode PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src/dll/BSAudioDecode.def")
    # Static imports resolve from System32 only.
    target_link_options(BSAudioDecode PRIVATE /DEPENDENTLOADFLAG:0x800)
elseif(ANDROID)
    target_link_options(BSAudioDecode PRIVATE
        "-Wl,-z,max-page-size=16384" "-Wl,--no-undefined"
        "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/src/dll/bsad.map")
    set_target_properties(BSAudioDecode PROPERTIES LINK_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/dll/bsad.map")
endif()

add_executable(bsadtool "${CMAKE_CURRENT_SOURCE_DIR}/tools/bsadtool/main.cpp" ${BSAD_ALL_SOURCES})
target_link_libraries(bsadtool PRIVATE bsad_common ${BSAD_PLATFORM_LIBRARIES})
if(MSVC)
    target_compile_definitions(bsadtool PRIVATE _CRT_SECURE_NO_WARNINGS)
elseif(ANDROID)
    target_link_libraries(bsadtool PRIVATE dl)
    target_link_options(bsadtool PRIVATE "-Wl,-z,max-page-size=16384")
endif()
