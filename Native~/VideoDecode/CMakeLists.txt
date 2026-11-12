cmake_minimum_required(VERSION 3.21)
cmake_policy(SET CMP0091 NEW)

project(BSVideoDecode LANGUAGES C CXX)

# Targets
#   BSVideoDecode   shared  src/core + src/win (WIN32) | src/android (ANDROID)
#                           -> BSVideoDecode.dll / libBSVideoDecode.so
#                           exports: BSVD_* of include/bsvideodecode.h plus UnityPluginLoad / UnityPluginUnload
#   bsvdtool        exe     tools/bsvdtool + the same sources: drives the C API as the C# side does, with a stand-in
#                           for Unity's graphics device (on the PC, or over adb shell on a device)
#
# Platform parts live in src/<platform>/platform.cmake, which adds to BSVD_PLATFORM_SOURCES, BSVD_PLATFORM_LIBRARIES,
# BSVD_TOOL_SOURCES and BSVD_TOOL_LIBRARIES, and may define more steps (shaders, say) on bsvd_platform_generated.
#
# BSVD_UNITY_PLUGIN_API: the Unity Editor's Data/PluginAPI folder (build.ps1 passes the project's editor's).

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

if(NOT BSVD_UNITY_PLUGIN_API OR NOT EXISTS "${BSVD_UNITY_PLUGIN_API}/IUnityGraphics.h")
    message(FATAL_ERROR "Set BSVD_UNITY_PLUGIN_API to the Unity Editor's Data/PluginAPI folder (build.ps1 does).")
endif()

if(MSVC)
    # Static CRT: no VC++ redistributable dependency in the player.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_library(bsvd_common INTERFACE)
target_include_directories(bsvd_common INTERFACE
    "${CMAKE_CURRENT_SOURCE_DIR}/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
    "${BSVD_UNITY_PLUGIN_API}")
if(MSVC)
    target_compile_definitions(bsvd_common INTERFACE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0A00)
    target_compile_options(bsvd_common INTERFACE /W4 /WX /permissive- /utf-8 /Zc:__cplusplus /EHsc /guard:cf /sdl)
    target_link_options(bsvd_common INTERFACE /guard:cf /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA $<$<CONFIG:Release>:/OPT:REF /OPT:ICF>)
elseif(ANDROID)
    target_compile_options(bsvd_common INTERFACE
        -Wall -Wextra -Werror -Wno-unused-parameter -fstack-protector-strong
        -D__ANDROID_UNAVAILABLE_SYMBOLS_ARE_WEAK__ -Werror=unguarded-availability)
else()
    target_compile_options(bsvd_common INTERFACE -Wall -Wextra -Wno-unused-parameter)
endif()

file(GLOB BSVD_CORE_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/core/*.cpp")
set(BSVD_PLATFORM_SOURCES "")
set(BSVD_PLATFORM_LIBRARIES "")
set(BSVD_TOOL_SOURCES "")
set(BSVD_TOOL_LIBRARIES "")
add_custom_target(bsvd_platform_generated)

if(WIN32)
    include("${CMAKE_CURRENT_SOURCE_DIR}/src/win/platform.cmake")
elseif(ANDROID)
    include("${CMAKE_CURRENT_SOURCE_DIR}/src/android/platform.cmake")
else()
    message(FATAL_ERROR "BSVideoDecode builds for Windows and Android only")
endif()

add_library(BSVideoDecode SHARED ${BSVD_CORE_SOURCES} ${BSVD_PLATFORM_SOURCES})
add_dependencies(BSVideoDecode bsvd_platform_generated)
target_link_libraries(BSVideoDecode PRIVATE bsvd_common ${BSVD_PLATFORM_LIBRARIES})
if(MSVC)
    target_sources(BSVideoDecode PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src/dll/BSVideoDecode.def")
    # Static imports resolve from System32 only.
    target_link_options(BSVideoDecode PRIVATE /DEPENDENTLOADFLAG:0x800)
elseif(ANDROID)
    target_link_options(BSVideoDecode PRIVATE
        "-Wl,-z,max-page-size=16384" "-Wl,--no-undefined"
        "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/src/dll/bsvd.map")
    set_target_properties(BSVideoDecode PROPERTIES LINK_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/dll/bsvd.map")
endif()

if(BSVD_TOOL_SOURCES)
    add_executable(bsvdtool ${BSVD_TOOL_SOURCES} ${BSVD_CORE_SOURCES} ${BSVD_PLATFORM_SOURCES})
    add_dependencies(bsvdtool bsvd_platform_generated)
    target_compile_definitions(bsvdtool PRIVATE BSVD_TOOL=1)
    target_link_libraries(bsvdtool PRIVATE bsvd_common ${BSVD_PLATFORM_LIBRARIES} ${BSVD_TOOL_LIBRARIES})
    if(MSVC)
        target_compile_definitions(bsvdtool PRIVATE _CRT_SECURE_NO_WARNINGS)
    elseif(ANDROID)
        target_link_options(bsvdtool PRIVATE "-Wl,-z,max-page-size=16384")
    endif()
endif()
