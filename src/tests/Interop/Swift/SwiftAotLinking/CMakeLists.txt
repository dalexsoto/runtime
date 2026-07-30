project(SwiftAotLinking)
include ("${CLR_INTEROP_TEST_ROOT}/Interop.cmake")

set(SOURCE SwiftAotLinking)

if (NOT SWIFT_COMPILER_TARGET AND CLR_CMAKE_TARGET_APPLE)
    set(SWIFT_PLATFORM "macosx")
    set(SWIFT_PLATFORM_SUFFIX "")
    if (NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0")
    endif()
    set(SWIFT_DEPLOYMENT_TARGET ${CMAKE_OSX_DEPLOYMENT_TARGET})
    set(SWIFT_COMPILER_TARGET "${CMAKE_OSX_ARCHITECTURES}-apple-${SWIFT_PLATFORM}${SWIFT_DEPLOYMENT_TARGET}${SWIFT_PLATFORM_SUFFIX}")
endif()

# The static fixture is linked into NativeAOT test binaries; the system Swift
# runtime it autolinks is bound through the SDK's TBD stub.
# Resolve the SDK explicitly: `xcrun --show-sdk-path` can return the
# CommandLineTools SDK even under DEVELOPER_DIR, which is a different SDK.
execute_process(COMMAND xcrun --sdk macosx --show-sdk-path OUTPUT_VARIABLE OSX_SDK_PATH OUTPUT_STRIP_TRAILING_WHITESPACE)

add_custom_target(${SOURCE} ALL
    COMMAND xcrun swiftc -target ${SWIFT_COMPILER_TARGET} -enable-library-evolution -module-name ${SOURCE} -emit-library ${CMAKE_CURRENT_SOURCE_DIR}/${SOURCE}.swift -o ${CMAKE_CURRENT_BINARY_DIR}/lib${SOURCE}.dylib -Xlinker -install_name -Xlinker @rpath/lib${SOURCE}.dylib
    COMMAND xcrun swiftc -target ${SWIFT_COMPILER_TARGET} -enable-library-evolution -module-name SwiftAotStatic -emit-library -static ${CMAKE_CURRENT_SOURCE_DIR}/SwiftAotStatic.swift -o ${CMAKE_CURRENT_BINARY_DIR}/libSwiftAotStatic.a
    COMMAND ${CMAKE_COMMAND} -E copy ${OSX_SDK_PATH}/usr/lib/swift/libswiftCore.tbd ${CMAKE_CURRENT_BINARY_DIR}/libswiftCore.tbd
    DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/${SOURCE}.swift ${CMAKE_CURRENT_SOURCE_DIR}/SwiftAotStatic.swift
    COMMENT "Generating ${SOURCE} libraries"
)

install(FILES ${CMAKE_CURRENT_BINARY_DIR}/lib${SOURCE}.dylib ${CMAKE_CURRENT_BINARY_DIR}/libSwiftAotStatic.a ${CMAKE_CURRENT_BINARY_DIR}/libswiftCore.tbd
    DESTINATION bin
)
