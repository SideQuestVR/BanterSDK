# Android (arm64-v8a, API 26+): MediaCodec through the NDK into an AImageReader; frames drawn on Unity's Vulkan device
# through a YCbCr sampler (BSVD_PATH_VULKAN) or converted on the CPU (BSVD_PATH_CPU). Included by ../../CMakeLists.txt.
#
# The two shaders are compiled to SPIR-V with the NDK's glslc into the build directory and included as C arrays.

set(BSVD_ANDROID_DIR "${CMAKE_CURRENT_LIST_DIR}")
set(BSVD_ANDROID_SHADER_OUT "${CMAKE_CURRENT_BINARY_DIR}/android-shaders")

file(GLOB BSVD_ANDROID_SOURCES CONFIGURE_DEPENDS "${BSVD_ANDROID_DIR}/*.cpp")
list(APPEND BSVD_PLATFORM_SOURCES ${BSVD_ANDROID_SOURCES})
# No libvulkan: every Vulkan call goes through Unity's own instance (or the test tool's).
list(APPEND BSVD_PLATFORM_LIBRARIES mediandk nativewindow log)

file(GLOB BSVD_ANDROID_TOOL_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/tools/bsvdtool/android*.cpp")
list(APPEND BSVD_TOOL_SOURCES ${BSVD_ANDROID_TOOL_SOURCES})
list(APPEND BSVD_TOOL_LIBRARIES vulkan dl)

# Ninja on Windows names a compiler path with spaces by its 8.3 short name (CLANG_~1.EXE), so clang can't tell from its
# name that it is clang++, links as C and drops libc++. Forcing the driver mode restores C++ linking.
if(CMAKE_HOST_WIN32)
    target_link_options(bsvd_common INTERFACE "--driver-mode=g++")
endif()

# glslc ships in the NDK as shader-tools/<host>/glslc(.exe).
if(NOT BSVD_GLSLC)
    file(GLOB BSVD_GLSLC_CANDIDATES "${ANDROID_NDK}/shader-tools/*/glslc*")
    list(LENGTH BSVD_GLSLC_CANDIDATES BSVD_GLSLC_COUNT)
    if(BSVD_GLSLC_COUNT EQUAL 0)
        message(FATAL_ERROR "glslc not found under ${ANDROID_NDK}/shader-tools (set BSVD_GLSLC)")
    endif()
    list(GET BSVD_GLSLC_CANDIDATES 0 BSVD_GLSLC)
endif()

set(BSVD_ANDROID_SHADER_INCS "")
foreach(shader frame.vert frame.frag)
    set(source "${BSVD_ANDROID_DIR}/shaders/${shader}")
    set(output "${BSVD_ANDROID_SHADER_OUT}/${shader}.inc")
    add_custom_command(
        OUTPUT "${output}"
        COMMAND "${CMAKE_COMMAND}" -E make_directory "${BSVD_ANDROID_SHADER_OUT}"
        COMMAND "${BSVD_GLSLC}" -O --target-env=vulkan1.1 -mfmt=c "${source}" -o "${output}"
        DEPENDS "${source}"
        COMMENT "glslc ${shader}"
        VERBATIM)
    list(APPEND BSVD_ANDROID_SHADER_INCS "${output}")
endforeach()
add_custom_target(bsvd_android_shaders DEPENDS ${BSVD_ANDROID_SHADER_INCS})
add_dependencies(bsvd_platform_generated bsvd_android_shaders)
target_include_directories(bsvd_common INTERFACE "${BSVD_ANDROID_SHADER_OUT}")
