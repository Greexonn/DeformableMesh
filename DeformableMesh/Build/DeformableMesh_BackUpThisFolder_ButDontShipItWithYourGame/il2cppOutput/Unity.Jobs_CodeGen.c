#include "il2cpp-config.h"

#ifndef _MSC_VER
# include <alloca.h>
#else
# include <malloc.h>
#endif



#include "codegen/il2cpp-codegen-metadata.h"





IL2CPP_EXTERN_C_BEGIN
IL2CPP_EXTERN_C_END




// 0x00000001 System.Void Unity.Jobs.IJobParallelForBatch::Execute(System.Int32,System.Int32)
// 0x00000002 Unity.Jobs.JobHandle Unity.Jobs.IJobParallelForBatchExtensions::ScheduleBatch(T,System.Int32,System.Int32,Unity.Jobs.JobHandle)
// 0x00000003 System.IntPtr Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1::Initialize()
// 0x00000004 System.Void Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1::Execute(T&,System.IntPtr,System.IntPtr,Unity.Jobs.LowLevel.Unsafe.JobRanges&,System.Int32)
// 0x00000005 System.Void Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1_ExecuteJobFunction::.ctor(System.Object,System.IntPtr)
// 0x00000006 System.Void Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1_ExecuteJobFunction::Invoke(T&,System.IntPtr,System.IntPtr,Unity.Jobs.LowLevel.Unsafe.JobRanges&,System.Int32)
// 0x00000007 System.IAsyncResult Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1_ExecuteJobFunction::BeginInvoke(T&,System.IntPtr,System.IntPtr,Unity.Jobs.LowLevel.Unsafe.JobRanges&,System.Int32,System.AsyncCallback,System.Object)
// 0x00000008 System.Void Unity.Jobs.IJobParallelForBatchExtensions_ParallelForBatchJobStruct`1_ExecuteJobFunction::EndInvoke(T&,Unity.Jobs.LowLevel.Unsafe.JobRanges&,System.IAsyncResult)
static Il2CppMethodPointer s_methodPointers[8] = 
{
	NULL,
	NULL,
	NULL,
	NULL,
	NULL,
	NULL,
	NULL,
	NULL,
};
static const int32_t s_InvokerIndices[8] = 
{
	172,
	-1,
	-1,
	-1,
	-1,
	-1,
	-1,
	-1,
};
static const Il2CppTokenRangePair s_rgctxIndices[2] = 
{
	{ 0x02000004, { 3, 6 } },
	{ 0x06000002, { 0, 3 } },
};
static const Il2CppRGCTXDefinition s_rgctxValues[9] = 
{
	{ (Il2CppRGCTXDataType)3, 16823 },
	{ (Il2CppRGCTXDataType)3, 16824 },
	{ (Il2CppRGCTXDataType)2, 21184 },
	{ (Il2CppRGCTXDataType)2, 21185 },
	{ (Il2CppRGCTXDataType)1, 19519 },
	{ (Il2CppRGCTXDataType)3, 16825 },
	{ (Il2CppRGCTXDataType)2, 21186 },
	{ (Il2CppRGCTXDataType)3, 16826 },
	{ (Il2CppRGCTXDataType)2, 19519 },
};
extern const Il2CppCodeGenModule g_Unity_JobsCodeGenModule;
const Il2CppCodeGenModule g_Unity_JobsCodeGenModule = 
{
	"Unity.Jobs.dll",
	8,
	s_methodPointers,
	s_InvokerIndices,
	0,
	NULL,
	2,
	s_rgctxIndices,
	9,
	s_rgctxValues,
	NULL,
};
