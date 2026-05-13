namespace BackendApi.Core.Attributes;

/// <summary>
/// ใส่ไว้บน Action Method หรือ Controller เพื่อ Bypass GlobalResponseFilter
/// ใช้กับเคสที่ต้องคืนค่าเป็น Raw Data เช่น PDF, Excel, FileStreamResult
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class DisableWrapperAttribute : Attribute
{
}
