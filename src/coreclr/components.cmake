# Define all the individually buildable components of the CoreCLR build and their respective targets
add_component(jit)
add_component(wasmjit)
add_component(alljits)
add_component(hosts)
add_component(runtime)
add_component(paltests paltests_install)
add_component(iltools)
add_component(nativeaot)
add_component(spmi)
add_component(debug)
add_component(cdac)

# Define coreclr_all as the fallback component and make every component depend on this component.
# iltools and paltests should be minimal subsets, so don't add a dependency on coreclr_misc
set(CMAKE_INSTALL_DEFAULT_COMPONENT_NAME coreclr_misc)
add_component(coreclr_misc)
add_dependencies(runtime coreclr_misc)

# watchOS uses the interpreter and precompiled code, not a target-side JIT.
add_dependencies(runtime iltools)
if(NOT CLR_CMAKE_TARGET_WATCHOS)
  add_dependencies(runtime jit)
endif()

# The runtime build requires the debugger tools builds
add_dependencies(runtime debug)

add_dependencies(runtime hosts)
