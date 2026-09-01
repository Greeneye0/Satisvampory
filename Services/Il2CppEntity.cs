using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using System;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class Il2CppEntity
    {
        static EntityManager Em => Core.Server.EntityManager;

        public static unsafe void Write<T>(Entity entity, T data) where T : struct
        {
            var type = new ComponentType(Il2CppType.Of<T>());
            var bytes = ToBytes(data);
            var size = Marshal.SizeOf<T>();
            fixed (byte* p = bytes)
                Em.SetComponentDataRaw(entity, type.TypeIndex, p, size);
        }

        static byte[] ToBytes<T>(T value) where T : struct
        {
            var size = Marshal.SizeOf(value);
            var bytes = new byte[size];
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(value, ptr, true);
            Marshal.Copy(ptr, bytes, 0, size);
            Marshal.FreeHGlobal(ptr);
            return bytes;
        }

        public static unsafe T Read<T>(Entity entity) where T : struct
        {
            var type = new ComponentType(Il2CppType.Of<T>());
            var raw = Em.GetComponentDataRawRO(entity, type.TypeIndex);
            return Marshal.PtrToStructure<T>(new IntPtr(raw));
        }

        public static DynamicBuffer<T> Buffer<T>(Entity entity) where T : struct
            => Em.GetBuffer<T>(entity);

        public static bool Has<T>(Entity entity)
            => Em.HasComponent(entity, new ComponentType(Il2CppType.Of<T>()));

        public static void Add<T>(Entity entity)
            => Em.AddComponent(entity, new ComponentType(Il2CppType.Of<T>()));

        public static void Remove<T>(Entity entity)
            => Em.RemoveComponent(entity, new ComponentType(Il2CppType.Of<T>()));

        public static string LookupName(PrefabGUID prefab)
        {
            var catalog = Core.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            return catalog._PrefabLookupMap.TryGetName(prefab, out var name)
                ? name + " " + prefab
                : "GUID Not Found";
        }

        public static string PrefabName(PrefabGUID prefab)
        {
            var localized = Core.Localization.GetPrefabName(prefab);
            return string.IsNullOrEmpty(localized) ? LookupName(prefab) : localized;
        }

        public static string EntityName(Entity entity)
        {
            var plate = Read<NameableInteractable>(entity).Name.ToString();
            if (string.IsNullOrEmpty(plate) && Has<PrefabGUID>(entity))
                plate = PrefabName(Read<PrefabGUID>(entity));
            return string.IsNullOrEmpty(plate) ? entity.ToString() : plate;
        }
    }
}
