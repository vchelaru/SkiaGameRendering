using System.Runtime.CompilerServices;

namespace SkiaGameRendering.Core.ANGLE
{
    /// <summary>
    /// Raw COM vtable calls for the D3D11.1 state-swap interop in <see cref="AngleSkiaSurfaceFactory"/>.
    /// No interop library (SharpDX, Vortice, TerraFX...) on either side of this class - see that
    /// class's MAINTENANCE NOTES for why a hard PackageReference to any of them is wrong here.
    ///
    /// Vtable slot indices are derived from each interface's inheritance chain and declaration
    /// order in d3d11.h / d3d11_1.h (every COM interface starts with IUnknown's QueryInterface(0),
    /// AddRef(1), Release(2)). An off-by-one calls a neighboring method with the wrong signature,
    /// which corrupts the stack instead of failing loudly, so every index and signature here was
    /// cross-checked against github.com/terrafx/terrafx.interop.windows (headers auto-generated
    /// from the Windows SDK) before use. <c>tests/Tests.Core.ANGLE/D3D11StateSwapTests.cs</c> pins
    /// the two D3D11.1 calls against WARP.
    /// </summary>
    internal static unsafe class D3D11Com
    {
        internal static readonly Guid IID_ID3D11Device1 = new("a04bfb29-08ef-43d6-a49c-a9bdbdcbe686");
        internal static readonly Guid IID_ID3D11DeviceContext1 = new("bb2c6faa-b5fb-4082-8e6b-388b8cfa90e1");

        const uint D3D11_SDK_VERSION = 7;
        const int D3D_FEATURE_LEVEL_11_0 = 0xb000;

        /// <summary>IUnknown::QueryInterface, vtable slot 0. AddRefs on success.</summary>
        internal static IntPtr QueryInterface(IntPtr unknown, in Guid iid)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, Guid*, void**, int>)(*(void***)unknown)[0];
            void* result;
            fixed (Guid* iidPtr = &iid)
            {
                int hr = fn(unknown, iidPtr, &result);
                if (hr < 0)
                    throw new InvalidOperationException($"QueryInterface({iid}) failed. HRESULT: 0x{hr:X8}");
            }
            return (IntPtr)result;
        }

        /// <summary>IUnknown::Release, vtable slot 2.</summary>
        internal static uint Release(IntPtr unknown)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint>)(*(void***)unknown)[2];
            return fn(unknown);
        }

        /// <summary>
        /// ID3D11Device1::CreateDeviceContextState, vtable slot 47: IUnknown (0-2) + ID3D11Device's
        /// 40 methods (3-42) + GetImmediateContext1(43), CreateDeferredContext1(44),
        /// CreateBlendState1(45), CreateRasterizerState1(46), CreateDeviceContextState(47).
        ///
        /// Always requests feature level 11_0 and emulates ID3D11Device1 - the same fixed choice
        /// the SharpDX reflection this replaces made via <c>MakeGenericMethod(device1Type)</c>.
        /// </summary>
        internal static IntPtr CreateDeviceContextState(IntPtr device1)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, int*, uint, uint, Guid*, int*, void**, int>)
                (*(void***)device1)[47];

            int featureLevel = D3D_FEATURE_LEVEL_11_0;
            var emulatedInterface = IID_ID3D11Device1;
            int chosenLevel;
            void* state;
            int hr = fn(device1, 0, &featureLevel, 1, D3D11_SDK_VERSION, &emulatedInterface, &chosenLevel, &state);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D11Device1::CreateDeviceContextState failed. HRESULT: 0x{hr:X8}");
            return (IntPtr)state;
        }

        /// <summary>
        /// ID3D11DeviceContext1::SwapDeviceContextState, vtable slot 131: IUnknown (0-2) +
        /// ID3D11DeviceChild's 4 methods (3-6) + ID3D11DeviceContext's 108 methods (7-114) +
        /// ID3D11DeviceContext1's methods up to and including SwapDeviceContextState (115-131).
        /// Returns void on the native side - there is no HRESULT to check.
        /// </summary>
        internal static IntPtr SwapDeviceContextState(IntPtr context1, IntPtr newState)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, void**, void>)(*(void***)context1)[131];
            void* previous;
            fn(context1, newState, &previous);
            return (IntPtr)previous;
        }
    }
}
