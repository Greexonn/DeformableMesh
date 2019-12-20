using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using UnityEngine.Rendering;

public class DeformableMesh : MonoBehaviour
{
    [SerializeField] Material _meshMaterial;

    //
    [SerializeField] private float _cellSize;
    [SerializeField] private int3 _gridSize;

    private MeshFilter _meshFilter;

    private MeshGridData _cubesGrid;

    //for batches
    [SerializeField] private int _maxBatchCellSize;

    private MeshBatch[,,] _meshBatches;
    private int3 _batchesGridSize;
    private int _batchIndexesCount;
    private int _batchVerticesCount;

    //for jobs
    private NativeList<JobHandle> _jobsHandles;
    private NativeList<JobHandle> _jobsCutHandles;
    private NativeList<JobHandle> _jobsPullHandles;
    private NativeList<JobHandle> _jobsTriangulateHandles;
    private NativeList<JobHandle> _jobsUpdateBatchesHandles;

    void Update()
    {
        //debug
        // UpdateMesh(new int3(0, 0, 0), _gridSize);
    }

    void Start()
    {
        AllocateContainers();

        _cubesGrid.Create(_cellSize, _gridSize);

        InitializeBatchGrid();
        // _meshFilter = GetComponent<MeshFilter>();
        UpdateMesh(new int3(0, 0, 0), _gridSize);
        var _boxCollider = gameObject.AddComponent<BoxCollider>();
        _boxCollider.size = new Vector3(_gridSize.x * _cellSize, _gridSize.y * _cellSize, _gridSize.z * _cellSize);
        _boxCollider.center = new Vector3(_gridSize.x * _cellSize / 2, _gridSize.y * _cellSize / 2, _gridSize.z * _cellSize / 2);

        //

    }

    void OnDestroy()
    {
        _cubesGrid.Dispose();
        DisposeContainers();
    }

    private void AllocateContainers()
    {
        _jobsHandles = new NativeList<JobHandle>(Allocator.Persistent);
        _jobsCutHandles = new NativeList<JobHandle>(Allocator.Persistent);
        _jobsPullHandles = new NativeList<JobHandle>(Allocator.Persistent);
        _jobsTriangulateHandles = new NativeList<JobHandle>(Allocator.Persistent);
        _jobsUpdateBatchesHandles = new NativeList<JobHandle>(Allocator.Persistent);
    }

    private void DisposeContainers()
    {
        _jobsHandles.Dispose();
        _jobsCutHandles.Dispose();
        _jobsPullHandles.Dispose();
        _jobsTriangulateHandles.Dispose();
        _jobsUpdateBatchesHandles.Dispose();
    }

    private void UpdateMesh(int3 fromIds, int3 toIds)
    {
        _jobsTriangulateHandles.Clear();

        NativeArray<int3> _cellsMap = new NativeArray<int3>(_cubesGrid._cellsGrid.indexes.Length / 36, Allocator.TempJob);
        //re-create surface cells
        TriangulateCubeJob _triangulateJob = new TriangulateCubeJob
        {
            points = _cubesGrid._pointsGrid.points,
            pointsGridSize = _cubesGrid._pointsGrid.size,
            indexes = _cubesGrid._cellsGrid.indexes,
            cellsMap = _cellsMap
        };
        for (int x = fromIds.x; x < toIds.x; x++)
        {
            for (int y = fromIds.y; y < toIds.y; y++)
            {
                for (int z = fromIds.z; z < toIds.z; z++)
                {
                    int _index = (x * _gridSize.y * _gridSize.z) + (y * _gridSize.z) + z;
                    _cellsMap[_index] = new int3(x, y, z);
                }
            }
        }
        var _handle = _triangulateJob.ScheduleBatch(_triangulateJob.indexes.Length, 36, JobHandle.CombineDependencies(_jobsCutHandles));
        _jobsTriangulateHandles.Add(_handle);
        _jobsHandles.AddRange(_jobsTriangulateHandles);
        
        // _cubesGrid.GetMesh(_meshFilter, _jobsHandles);

        //batches
        UpdateBatches(int3.zero, new int3(1, 1, 1));
    }

    public void CutSphere(float3 sphereCenter, float sphereRadius)
    {
        _jobsHandles.Clear();
        _jobsCutHandles.Clear();
        _jobsPullHandles.Clear();

        NativeArray<int3> _pointsMap = new NativeArray<int3>(_cubesGrid._pointsGrid.points.Length, Allocator.TempJob);

        //find cubes to cut
        int _cellsCount = (int)(sphereRadius / _cellSize + 1);
        int3 _from, _to;
        int3 _cellId = (int3)(sphereCenter / _cellSize);
        _from = math.clamp((_cellId - new int3(_cellsCount, _cellsCount, _cellsCount)), new int3(0, 0, 0), _gridSize);
        _to = math.clamp((_cellId + new int3(_cellsCount, _cellsCount, _cellsCount)), new int3(0, 0, 0), _gridSize);

        //create job for calculations
        CutSphereJob _cutJob = new CutSphereJob
        {
            points = _cubesGrid._pointsGrid.points,
            sphereCenter = sphereCenter,
            sphereRadius = sphereRadius,
            cellSize = _cellSize,
            pointsMap = _pointsMap
        };
        //iterate in active points
        for (int x = _from.x; x < (_to.x + 1); x++)
        {
            for (int y = _from.y; y < (_to.y + 1); y++)
            {
                for (int z = _from.z; z < (_to.z + 1); z++)
                {
                    int _index = (x * (_gridSize.y + 1) * (_gridSize.z + 1)) + (y * (_gridSize.z + 1)) + z;
                    _pointsMap[_index] = new int3(x, y, z);
                }
            }
        }
        var _handle = _cutJob.Schedule(_cubesGrid._pointsGrid.points.Length, (_cubesGrid._pointsGrid.points.Length / 10));
        _jobsCutHandles.Add(_handle);
        _jobsHandles.AddRange(_jobsCutHandles);

        //pull vertices
        PullVerticesJob _pullVerticesJob = new PullVerticesJob
        {
            points = _cubesGrid._pointsGrid.points,
            pointsMap = _pointsMap,
            cellSize = _cellSize,
            gridSize = _gridSize,
            vertices = _cubesGrid._verticesArray
        };

        _handle = _pullVerticesJob.Schedule(_cubesGrid._pointsGrid.points.Length, (_cubesGrid._pointsGrid.points.Length / 10), JobHandle.CombineDependencies(_jobsCutHandles));
        _jobsPullHandles.Add(_handle);
        _jobsHandles.AddRange(_jobsPullHandles);

        UpdateMesh(_from, _to);
    }

    private void InitializeBatchGrid()
    {
        //find batches grid size
        //find x
        _batchesGridSize.x = _gridSize.x / _maxBatchCellSize;
        if (_gridSize.x % _maxBatchCellSize != 0)
            _batchesGridSize.x++;
        //find y
        _batchesGridSize.y = _gridSize.y / _maxBatchCellSize;
        if (_gridSize.y % _maxBatchCellSize != 0)
            _batchesGridSize.y++;
        //find z
        _batchesGridSize.z = _gridSize.z / _maxBatchCellSize;
        if (_gridSize.z % _maxBatchCellSize != 0)
            _batchesGridSize.z++;

        //find max indexes and vertices count for batch
        _batchIndexesCount = _maxBatchCellSize * _maxBatchCellSize * _maxBatchCellSize * 36;
        _batchVerticesCount = (_maxBatchCellSize + 1) * (_maxBatchCellSize + 1) * (_maxBatchCellSize + 1);

        //create batches grid
        _meshBatches = new MeshBatch[_batchesGridSize.x, _batchesGridSize.y, _batchesGridSize.z];
        for (int x = 0; x < _batchesGridSize.x; x++)
        {
            for (int y = 0; y < _batchesGridSize.y; y++)
            {
                for (int z = 0; z < _batchesGridSize.z; z++)
                {
                    _meshBatches[x, y, z].instance = new GameObject("Batch: " + new int3(x, y, z).ToString());
                    _meshBatches[x, y, z].instance.transform.parent = transform;
                    //set position
                    float _batchSideSize = _maxBatchCellSize * _cellSize;
                    Vector3 _pos = new Vector3((x * _batchSideSize), (y * _batchSideSize), (z * _batchSideSize));
                    _meshBatches[x, y, z].instance.transform.localPosition = _pos;
                    //add mesh components
                    _meshBatches[x, y, z].meshFilter = _meshBatches[x, y, z].instance.AddComponent<MeshFilter>();
                    _meshBatches[x, y, z].instance.AddComponent<MeshRenderer>().sharedMaterial = _meshMaterial;
                }
            }
        }
    }

    private void UpdateBatches(int3 fromIds, int3 toIds)
    {
        _jobsUpdateBatchesHandles.Clear();

        CopyBatchIndexesJob _copyBatchIndexesJob = new CopyBatchIndexesJob
        {
            indexes = _cubesGrid._cellsGrid.indexes,
            gridSize = _gridSize,
            batchSize = _maxBatchCellSize
        };

        CopyBatchVerticesJob _copyBatchVerticesJob = new CopyBatchVerticesJob
        {
            vertices = _cubesGrid._verticesArray,
            gridSize = _gridSize,
            cellSize = _cellSize,
            batchSize = _maxBatchCellSize
        };

        var _dependencyIndexes = JobHandle.CombineDependencies(_jobsTriangulateHandles);
        var _dependencyVertices = JobHandle.CombineDependencies(_jobsPullHandles);

        for (int x = fromIds.x; x < toIds.x; x++)
        {
            for (int y = fromIds.y; y < toIds.y; y++)
            {
                for (int z = fromIds.z; z < toIds.z; z++)
                {
                    int3 _batchId = new int3(x, y, z);
                    _meshBatches[x, y, z].batchIndexes = new NativeList<int>(_batchIndexesCount, Allocator.TempJob);
                    _meshBatches[x, y, z].batchVertices = new NativeList<float3>(_batchVerticesCount, Allocator.TempJob);
                    //schedule job indexes
                    _copyBatchIndexesJob.batchId = _batchId;
                    _copyBatchIndexesJob.batchIndexes = _meshBatches[x, y, z].batchIndexes;
                    var _handle = _copyBatchIndexesJob.Schedule(_dependencyIndexes);
                    _jobsUpdateBatchesHandles.Add(_handle);
                    //schedule job vertices
                    _copyBatchVerticesJob.batchId = _batchId;
                    _copyBatchVerticesJob.batchVertices = _meshBatches[x, y, z].batchVertices;
                    _handle = _copyBatchVerticesJob.Schedule(_dependencyVertices);
                    _jobsUpdateBatchesHandles.Add(_handle);
                }
            }
        }

        _jobsHandles.AddRange(_jobsUpdateBatchesHandles);
        UpdateBatchesMeshes(fromIds, toIds);
    }

    private void UpdateBatchesMeshes(int3 fromIds, int3 toIds)
    {
        JobHandle.CompleteAll(_jobsHandles);

        //update meshes
        for (int x = fromIds.x; x < toIds.x; x++)
        {
            for (int y = fromIds.y; y < toIds.y; y++)
            {
                for (int z = fromIds.z; z < toIds.z; z++)
                {
                    if (_meshBatches[x, y, z].batchIndexes.Length > 0) //check if any indexes were added
                    {
                        if (_meshBatches[x, y, z].meshFilter.sharedMesh == null)
                            _meshBatches[x, y, z].meshFilter.sharedMesh = new Mesh();

                        //update batch vertices and indexes
                        var _mesh = _meshBatches[x, y, z].meshFilter.sharedMesh;
                        int _indexesCount = _meshBatches[x, y, z].batchIndexes.Length;
                        int _vertexCount = _meshBatches[x, y, z].batchVertices.Length;
                        _mesh.SetVertexBufferParams(_vertexCount, _cubesGrid._layout);
                        _mesh.SetVertexBufferData(_meshBatches[x, y, z].batchVertices.AsArray(), 0, 0, _vertexCount);
                        _mesh.SetIndexBufferParams(_indexesCount, IndexFormat.UInt32);
                        _mesh.SetIndexBufferData(_meshBatches[x, y, z].batchIndexes.AsArray(), 0, 0, _indexesCount);
                        SubMeshDescriptor _smd = new SubMeshDescriptor(0, _indexesCount);
                        _mesh.subMeshCount = 1;
                        _mesh.SetSubMesh(0, _smd);
                        _mesh.RecalculateBounds();
                        _mesh.RecalculateNormals();
                        _mesh.RecalculateTangents();
                    }
                    //deallocate temporal containers
                    _meshBatches[x, y, z].batchIndexes.Dispose();
                    _meshBatches[x, y, z].batchVertices.Dispose();
                }
            }
        }
    }


    public struct MeshBatch
    {
        public GameObject instance;
        public MeshFilter meshFilter;
        public NativeList<int> batchIndexes;
        public NativeList<float3> batchVertices;
    }

    public struct MeshGridData : System.IDisposable
    {
        public float cellSize;
        public int3 gridSize;

        private Mesh _mesh;

        public VertexAttributeDescriptor[] _layout;


        #region Native

        public NativeCubeGrid _pointsGrid;

        public NativeCellGrid _cellsGrid;

        public NativeArray<float3> _verticesArray;
        public NativeList<int> _indexesList;
            
        #endregion


        public void Create(float cellSize, int3 gridSize)
        {
            this.cellSize = cellSize;
            this.gridSize = gridSize;

            //create points buffer
            _pointsGrid = new NativeCubeGrid(gridSize.x + 1, gridSize.y + 1, gridSize.z + 1, Allocator.Persistent);
            _cellsGrid = new NativeCellGrid(gridSize, Allocator.Persistent);
            _verticesArray = new NativeArray<float3>(_pointsGrid.points.Length, Allocator.Persistent);

            StorePointsAndVertices();
        }

        public void Dispose()
        {
            _pointsGrid.Dispose();
            _cellsGrid.Dispose();
            _verticesArray.Dispose();
        }

        private void StorePointsAndVertices()
        {
            int _index = 0;
            for (int x = 0; x < (gridSize.x + 1); x++)
            {
                for (int y = 0; y < (gridSize.y + 1); y++)
                {
                    for (int z = 0; z < (gridSize.z + 1); z++)
                    {
                        int3 _pointId = new int3(x, y, z);
                        float3 _pointPosition = GetPointPosition(_pointId);
                        //add to vertices
                        _verticesArray[_index] = _pointPosition;
                        _index++;
                    }
                }
            }

            //

            _mesh = new Mesh();

            _layout = new VertexAttributeDescriptor[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
            };
            _mesh.SetVertexBufferParams(_verticesArray.Length, _layout);
            _mesh.SetVertexBufferData(_verticesArray, 0, 0, _verticesArray.Length);
        }

        public void GetMesh(MeshFilter filter, NativeList<JobHandle> jobHandles)
        {
            _indexesList = new NativeList<int>(_cellsGrid.indexes.Length, Allocator.TempJob);

            CopyIndexesToListJob _copyIndexesJob = new CopyIndexesToListJob
            {
                indexes = _cellsGrid.indexes,
                indexesList = _indexesList
            };

            var _handle = _copyIndexesJob.Schedule(JobHandle.CombineDependencies(jobHandles));
            jobHandles.Add(_handle);
            JobHandle.CompleteAll(jobHandles);

            _mesh.SetVertexBufferParams(_verticesArray.Length, _layout);
            _mesh.SetVertexBufferData(_verticesArray, 0, 0, _verticesArray.Length);
            _mesh.SetIndexBufferParams(_indexesList.Length, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(_indexesList.AsArray(), 0, 0, _indexesList.Length);
            SubMeshDescriptor _smd = new SubMeshDescriptor(0, _indexesList.Length);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, _smd);
            _mesh.RecalculateBounds();
            _mesh.RecalculateNormals();
            _mesh.RecalculateTangents();

            if (filter.sharedMesh == null)
                filter.sharedMesh = _mesh;

            _indexesList.Dispose();
        }
        
        public float3 GetPointPosition(int3 pointId)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }
    }

    public struct NativeCubeGrid : System.IDisposable
    {
        public NativeArray<byte> points;

        public int3 size;

        public NativeCubeGrid(int x, int y, int z, Allocator allocator)
        {
            this.size = new int3(x, y, z);
            points = new NativeArray<byte>((x * y * z), allocator);
            SetAllFull();
        }

        public NativeCubeGrid(int3 size, Allocator allocator)
        {
            this.size = size;
            points = new NativeArray<byte>((size.x * size.y * size.z), allocator);
            SetAllFull();
        }

        public void Dispose()
        {
            points.Dispose();
        }

        public byte this[int x, int y, int z]
        {
            get
            {
                int _index = (x * size.y * size.z) + (y * size.z) + z;
                return points[_index];
            }

            set
            {
                int _index = (x * size.y * size.z) + (y * size.z) + z;
                points[_index] = value;
            }
        }

        public byte this[int3 ids]
        {
            get
            {
                int _index = (ids.x * size.y * size.z) + (ids.y * size.z) + ids.z;
                return points[_index];
            }

            set
            {
                int _index = (ids.x * size.y * size.z) + (ids.y * size.z) + ids.z;
                points[_index] = value;
            }
        }
    
        private void SetAllFull()
        {
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = 255;
            }
        }
    }

    public struct NativeCellGrid : System.IDisposable
    {
        public NativeArray<int> indexes;
        public int3 size;

        public NativeCellGrid(int x, int y, int z, Allocator allocator)
        {
            this.size = new int3(x, y, z);
            int _arraySize = (x * y * z) * 36;
            indexes = new NativeArray<int>(_arraySize, allocator);
        }

        public NativeCellGrid(int3 size, Allocator allocator)
        {
            this.size = size;
            int _arraySize = (size.x * size.y * size.z) * 36;
            indexes = new NativeArray<int>(_arraySize, allocator);
        }

        public void Dispose()
        {
            indexes.Dispose();
        }

        private void FillAllNull()
        {
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = -1;
            }
        }
    
        public int GetCellStartIndex(int3 cellIds)
        {
            return ((cellIds.x * size.y * size.z) + (cellIds.y * size.z) + cellIds.z) * 36;
        }
    }

    public struct Cube
    {
        public int3 zero, one, two, three, four, five, six, seven;
        public bool isZero, isOne, isTwo, isThree, isFour, isFive, isSix, isSeven;
    }

    public struct Face
    {
        public int3 zero, one, two, three;
        public bool isZero, isOne, isTwo, isThree;
    }

    #region Jobs

    [BurstCompile]
    private struct CutSphereJob : IJobParallelFor
    {
        public NativeArray<byte> points;

        [ReadOnly] public NativeArray<int3> pointsMap;

        public float3 sphereCenter;
        public float cellSize, sphereRadius;

        public void Execute(int index)
        {
            int3 _currentPointId = pointsMap[index];
            if (_currentPointId.Equals(int3.zero) && index != 0)
                return;


            if (points[index] > 0)
            {
                float3 _toPoint = GetPointPosition(_currentPointId) - sphereCenter;
                _toPoint *= _toPoint; //square
                float _dist = math.sqrt(_toPoint.x + _toPoint.y + _toPoint.z);
                _dist -= sphereRadius;
                _dist = cellSize + _dist;
                _dist = _dist / cellSize;
                float _value = math.clamp(_dist, 0, 1);

                byte _pValue = (byte)math.lerp(0, 255, _value);
                if (_pValue < 130)
                    _pValue = 0;
                if (_pValue < points[index])
                    points[index] = _pValue;
            }
        }

        private float3 GetPointPosition(int3 pointId)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }
    }

    [BurstCompile]
    private struct PullVerticesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> points;
        [ReadOnly][DeallocateOnJobCompletion] public NativeArray<int3> pointsMap;

        public float cellSize;
        public int3 gridSize;

        [WriteOnly] public NativeArray<float3> vertices;

        public void Execute(int index)
        {
            int3 _currentPointId = pointsMap[index];
            if (_currentPointId.Equals(int3.zero) && index != 0)
                return;

            float3 _vertexPos = GetPointPosition(_currentPointId);
            float3 _pointPos = _vertexPos;
            float3 _pullVector = float3.zero;
            float _pullValue = 1 - ((float)points[index] / 255);
            int3 _firstAnchorId, _secondAnchorId;
            float3 _firstAnchor, _secondAnchor;

            //pull on x
            _firstAnchorId = math.clamp(_currentPointId + new int3(1, 0, 0), int3.zero, gridSize);
            _secondAnchorId = math.clamp(_currentPointId + new int3(-1, 0, 0), int3.zero, gridSize);
            _firstAnchor = GetPointPosition(_firstAnchorId);
            _secondAnchor = GetPointPosition(_secondAnchorId);
            //pull to first
            _pullVector = (_firstAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;
            //pull to second
            _pullVector = (_secondAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;

            //pull on y
            _firstAnchorId = math.clamp(_currentPointId + new int3(0, 1, 0), int3.zero, gridSize);
            _secondAnchorId = math.clamp(_currentPointId + new int3(0, -1, 0), int3.zero, gridSize);
            _firstAnchor = GetPointPosition(_firstAnchorId);
            _secondAnchor = GetPointPosition(_secondAnchorId);
            //pull to first
            _pullVector = (_firstAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;
            //pull to second
            _pullVector = (_secondAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;

            //pull on z
            _firstAnchorId = math.clamp(_currentPointId + new int3(0, 0, 1), int3.zero, gridSize);
            _secondAnchorId = math.clamp(_currentPointId + new int3(0, 0, -1), int3.zero, gridSize);
            _firstAnchor = GetPointPosition(_firstAnchorId);
            _secondAnchor = GetPointPosition(_secondAnchorId);
            //pull to first
            _pullVector = (_firstAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;
            //pull to second
            _pullVector = (_secondAnchor - _vertexPos) * _pullValue;
            _vertexPos += _pullVector;

            //write new vertex pos
            vertices[index] = _vertexPos;
        }

        private float3 GetPointPosition(int3 pointId)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }
    }

    [BurstCompile]
    private struct TriangulateCubeJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<byte> points;
        [ReadOnly] public int3 pointsGridSize;

        [ReadOnly][DeallocateOnJobCompletion] public NativeArray<int3> cellsMap;

        [WriteOnly] public NativeArray<int> indexes;

        public void Execute(int startIndex, int count)
        {
            // bool _miss = true;
            int3 _currentCellId = cellsMap[startIndex / 36];
            if (_currentCellId.Equals(int3.zero) && startIndex != 0)
                return;

            //get cell points
            Cube _cube = new Cube
            {
                zero = _currentCellId,
                one = new int3(_currentCellId.x, _currentCellId.y + 1, _currentCellId.z),
                two = new int3(_currentCellId.x + 1, _currentCellId.y + 1, _currentCellId.z),
                three = new int3(_currentCellId.x + 1, _currentCellId.y, _currentCellId.z),
                four = new int3(_currentCellId.x, _currentCellId.y, _currentCellId.z + 1),
                five = new int3(_currentCellId.x, _currentCellId.y + 1, _currentCellId.z + 1),
                six = new int3(_currentCellId.x + 1, _currentCellId.y + 1, _currentCellId.z + 1),
                seven = new int3(_currentCellId.x + 1, _currentCellId.y, _currentCellId.z + 1)
            };
            //get points states
            _cube.isZero = GetPointActive(_cube.zero);
            _cube.isOne = GetPointActive(_cube.one);
            _cube.isTwo = GetPointActive(_cube.two);
            _cube.isThree = GetPointActive(_cube.three);
            _cube.isFour = GetPointActive(_cube.four);
            _cube.isFive = GetPointActive(_cube.five);
            _cube.isSix = GetPointActive(_cube.six);
            _cube.isSeven = GetPointActive(_cube.seven);

            //triangulate faces
            int3 _normal;
            Face _face;
            Face _faceM;
            //front face
            _normal = new int3(0, 0, -1);
            _face = new Face
            {
                zero = _cube.zero,
                one = _cube.one,
                two = _cube.two,
                three = _cube.three,
                isZero = _cube.isZero,
                isOne = _cube.isOne,
                isTwo = _cube.isTwo,
                isThree = _cube.isThree
            };
            _faceM = new Face
            {
                zero = _cube.four,
                one = _cube.five,
                two = _cube.six,
                three = _cube.seven,
                isZero = _cube.isFour,
                isOne = _cube.isFive,
                isTwo = _cube.isSix,
                isThree = _cube.isSeven
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
            startIndex += 6;

            //back face
            _normal = new int3(0, 0, 1);
            _face = new Face
            {
                zero = _cube.seven,
                one = _cube.six,
                two = _cube.five,
                three = _cube.four,
                isZero = _cube.isSeven,
                isOne = _cube.isSix,
                isTwo = _cube.isFive,
                isThree = _cube.isFour
            };
            _faceM = new Face
            {
                zero = _cube.three,
                one = _cube.two,
                two = _cube.one,
                three = _cube.zero,
                isZero = _cube.isThree,
                isOne = _cube.isTwo,
                isTwo = _cube.isOne,
                isThree = _cube.isZero
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
            startIndex += 6;

            //left face
            _normal = new int3(-1, 0, 0);
            _face = new Face
            {
                zero = _cube.four,
                one = _cube.five,
                two = _cube.one,
                three = _cube.zero,
                isZero = _cube.isFour,
                isOne = _cube.isFive,
                isTwo = _cube.isOne,
                isThree = _cube.isZero
            };
            _faceM = new Face
            {
                zero = _cube.seven,
                one = _cube.six,
                two = _cube.two,
                three = _cube.three,
                isZero = _cube.isSeven,
                isOne = _cube.isSix,
                isTwo = _cube.isTwo,
                isThree = _cube.isThree
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
            startIndex += 6;

            //right face
            _normal = new int3(1, 0, 0);
            _face = new Face
            {
                zero = _cube.three,
                one = _cube.two,
                two = _cube.six,
                three = _cube.seven,
                isZero = _cube.isThree,
                isOne = _cube.isTwo,
                isTwo = _cube.isSix,
                isThree = _cube.isSeven
            };
            _faceM = new Face
            {
                zero = _cube.zero,
                one = _cube.one,
                two = _cube.five,
                three = _cube.four,
                isZero = _cube.isZero,
                isOne = _cube.isOne,
                isTwo = _cube.isFive,
                isThree = _cube.isFour
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
            startIndex += 6;

            //bottom face
            _normal = new int3(0, -1, 0);
            _face = new Face
            {
                zero = _cube.four,
                one = _cube.zero,
                two = _cube.three,
                three = _cube.seven,
                isZero = _cube.isFour,
                isOne = _cube.isZero,
                isTwo = _cube.isThree,
                isThree = _cube.isSeven
            };
            _faceM = new Face
            {
                zero = _cube.five,
                one = _cube.one,
                two = _cube.two,
                three = _cube.six,
                isZero = _cube.isFive,
                isOne = _cube.isOne,
                isTwo = _cube.isTwo,
                isThree = _cube.isSix
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
            startIndex += 6;

            //top face
            _normal = new int3(0, 1, 0);
            _face = new Face
            {
                zero = _cube.one,
                one = _cube.five,
                two = _cube.six,
                three = _cube.two,
                isZero = _cube.isOne,
                isOne = _cube.isFive,
                isTwo = _cube.isSix,
                isThree = _cube.isTwo
            };
            _faceM = new Face
            {
                zero = _cube.zero,
                one = _cube.four,
                two = _cube.seven,
                three = _cube.three,
                isZero = _cube.isZero,
                isOne = _cube.isFour,
                isTwo = _cube.isSeven,
                isThree = _cube.isThree
            };
            ClearFace(startIndex);
            if (IsFaceVisible(_face, _normal))
            {
                TriangulateFace(_face, _faceM, startIndex);
            }
        }

        private bool GetPointActive(int3 pointIds)
        {
            int _index = (pointIds.x * pointsGridSize.y * pointsGridSize.z) + (pointIds.y * pointsGridSize.z) + pointIds.z;
            return points[_index] > 0;
        }

        private int GetPointBufferIndex(int3 pointIds)
        {
            return (pointIds.x * pointsGridSize.y * pointsGridSize.z) + (pointIds.y * pointsGridSize.z) + pointIds.z;
        }

        private int GetCellBufferIndex(int3 cellIds)
        {
            return ((cellIds.x * (pointsGridSize.y - 1) * (pointsGridSize.z - 1)) + (cellIds.y * (pointsGridSize.z - 1)) + cellIds.z) * 36;
        }

        private bool IsFaceVisible(Face face, int3 normal)
        {
            //check if vertices visible
            if (IsVertexOpen(face.zero, normal))
                return true;
            else if (IsVertexOpen(face.one, normal))
                return true;
            else if (IsVertexOpen(face.two, normal))
                return true;
            else if (IsVertexOpen(face.three, normal))
                return true;

            return false;
        }

        private bool IsVertexOpen(int3 vertex, int3 normal)
        {
            var _pointId = vertex + normal;
            //check boundaries
            if (normal.x != 0)
                if (_pointId.x < 0 || _pointId.x == pointsGridSize.x)
                    return true;
            if (normal.y != 0)
                if (_pointId.y < 0 || _pointId.y == pointsGridSize.y)
                    return true;
            if (normal.z != 0)
                if (_pointId.z < 0 || _pointId.z == pointsGridSize.z)
                    return true;
            //check visible
            if (!GetPointActive(_pointId))
                return true;

            return false;
        }

        private void ClearFace(int startIndex)
        {
            for (int i = startIndex; i < (startIndex + 6); i++)
            {
                indexes[i] = -1;
            }
        }

        private void TriangulateFace(Face face, Face faceM, int startIndex)
        {
            //check face points
            if (!face.isZero && faceM.isZero)
            {
                face.isZero = true;
                face.zero = faceM.zero;
            }
            if (!face.isOne && faceM.isOne)
            {
                face.isOne = true;
                face.one = faceM.one;
            }
            if (!face.isTwo && faceM.isTwo)
            {
                face.isTwo = true;
                face.two = faceM.two;
            }
            if (!face.isThree && faceM.isThree)
            {
                face.isThree = true;
                face.three = faceM.three;
            }

            //try take first triangle
            if (face.isZero)
            {
                bool _oneFound = false;
                if (face.isOne)
                {
                    if (face.isTwo)
                    {
                        indexes[startIndex] = GetPointBufferIndex(face.zero);
                        indexes[startIndex + 1] = GetPointBufferIndex(face.one);
                        indexes[startIndex + 2] = GetPointBufferIndex(face.two);
                        _oneFound = true;
                    }
                }
                //try take second triangle
                if (face.isTwo)
                {
                    if (face.isThree)
                    {
                        indexes[startIndex + 3] = GetPointBufferIndex(face.zero);
                        indexes[startIndex + 4] = GetPointBufferIndex(face.two);
                        indexes[startIndex + 5] = GetPointBufferIndex(face.three);
                        _oneFound = true;
                    }
                }
                //try different pattern
                if (!_oneFound)
                {
                    if (face.isOne)
                    {
                        if (face.isThree)
                        {
                            indexes[startIndex] = GetPointBufferIndex(face.zero);
                            indexes[startIndex + 1] = GetPointBufferIndex(face.one);
                            indexes[startIndex + 2] = GetPointBufferIndex(face.three);
                        }
                    }
                }
            }
            else //try start from second
            {
                if (face.isOne)
                {
                    if (face.isTwo)
                    {
                        if (face.isThree)
                        {
                            indexes[startIndex] = GetPointBufferIndex(face.one);
                            indexes[startIndex + 1] = GetPointBufferIndex(face.two);
                            indexes[startIndex + 2] = GetPointBufferIndex(face.three);
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct CopyIndexesToListJob : IJob
    {
        [ReadOnly] public NativeArray<int> indexes;

        [WriteOnly] public NativeList<int> indexesList;

        public void Execute()
        {
            for (int i = 0; i < indexes.Length; i++)
            {
                int _index = indexes[i];
                if (_index >= 0)
                    indexesList.Add(_index);
            }
        }
    }

    [BurstCompile]
    private struct CopyBatchIndexesJob : IJob
    {
        [ReadOnly] public NativeArray<int> indexes;

        public int3 gridSize;
        public int3 batchId;
        public int batchSize;

        [WriteOnly] public NativeList<int> batchIndexes;

        public void Execute()
        {
            int3 _from = batchId * batchSize;
            int3 _to = math.clamp((_from + new int3(batchSize, batchSize, batchSize)), _from, gridSize);

            for (int x = _from.x; x < _to.x; x++)
            {
                for (int y = _from.y; y < _to.y; y++)
                {
                    for (int z = _from.z; z < _to.z; z++)
                    {
                        int _startIndex = GetCellStartIndex(new int3(x, y, z));
                        int _endIndex = _startIndex + 36;
                        int _startVertex = _startIndex / 36;
                        //copy cell indexes
                        for (int i = _startIndex; i < _endIndex; i++)
                        {
                            int _index = indexes[i];
                            int _cubeIndex = _index - _startVertex;
                            Debug.Log(_cubeIndex);
                            if (_index >= 0)
                                batchIndexes.Add(_cubeIndex);
                        }
                    }
                }
            }
        }

        private int GetCellStartIndex(int3 cellIds)
        {
            return ((cellIds.x * gridSize.y * gridSize.z) + (cellIds.y * gridSize.z) + cellIds.z) * 36;
        }
    }

    [BurstCompile]
    private struct CopyBatchVerticesJob : IJob
    {
        [ReadOnly] public NativeArray<float3> vertices;

        public int3 gridSize;
        public int3 batchId;
        public int batchSize;
        public float cellSize;

        [WriteOnly] public NativeList<float3> batchVertices;

        public void Execute()
        {
            int3 _from = batchId * batchSize;
            int3 _to = math.clamp((_from + new int3((batchSize + 1), (batchSize + 1), (batchSize + 1))), _from, (gridSize + new int3(1, 1, 1)));

            float3 _batchOffset = new float3((batchId.x * batchSize * cellSize), (batchId.y * batchSize * cellSize), (batchId.z * batchSize * cellSize));

            for (int x = _from.x; x < _to.x; x++)
            {
                for (int y = _from.y; y < _to.y; y++)
                {
                    for (int z = _from.z; z < _to.z; z++)
                    {
                        batchVertices.Add(vertices[GetPointIndex(new int3(x, y, z))] - _batchOffset);
                    }
                }
            }
        }

        private int GetPointIndex(int3 pointId)
        {
            return (pointId.x * (gridSize.y + 1) * (gridSize.z + 1)) + (pointId.y * (gridSize.z + 1)) + pointId.z;
        }
    }

    #endregion
}
