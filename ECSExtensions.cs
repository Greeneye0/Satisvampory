
namespace Satisvampory;

public static class ECSExtensions
{
    public static unsafe void Write<T>(this Entity entity, T componentData) where T : struct
        => Services.Il2CppEntity.Write(entity, componentData);

    public static unsafe T Read<T>(this Entity entity) where T : struct
        => Services.Il2CppEntity.Read<T>(entity);

    public static DynamicBuffer<T> ReadBuffer<T>(this Entity entity) where T : struct
        => Services.Il2CppEntity.Buffer<T>(entity);

    public static bool Has<T>(this Entity entity)
        => Services.Il2CppEntity.Has<T>(entity);

    public static void Add<T>(this Entity entity)
        => Services.Il2CppEntity.Add<T>(entity);

    public static void Remove<T>(this Entity entity)
        => Services.Il2CppEntity.Remove<T>(entity);

    public static string LookupName(this PrefabGUID prefabGuid)
        => Services.Il2CppEntity.LookupName(prefabGuid);

    public static string PrefabName(this PrefabGUID prefabGuid)
        => Services.Il2CppEntity.PrefabName(prefabGuid);

    public static string EntityName(this Entity entity)
        => Services.Il2CppEntity.EntityName(entity);
}
