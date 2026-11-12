using Xunit;

// Each test creates and destroys its own VkInstance and VkDevice. The Vulkan loader looks a device up by
// walking every instance's driver and device lists (vkGetDeviceProcAddr and other device trampolines), and
// that walk is not safe against another thread destroying an instance or device: under xunit's default,
// test classes run in parallel, and the test host segfaults inside libvulkan, intermittently. Serializing
// the assembly means only one instance is ever being created or destroyed at a time, and a future test class
// can't forget to opt in. Same blunt fix as StrideTestParallelism.cs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
