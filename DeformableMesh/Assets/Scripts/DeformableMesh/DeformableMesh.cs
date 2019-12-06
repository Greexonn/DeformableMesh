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
    //
    [SerializeField] private float _cellSize;
    [SerializeField] private int3 _gridSize;

    private MeshFilter _meshFilter;

    private MeshGridData _cubesGrid;

    //for jobs
    private NativeList<JobHandle> _jobsHandles;
    private NativeList<JobHandle> _jobsCutHandles;
    private NativeList<JobHandle> _jobsPullHandles;
    private NativeList<JobHandle> _jobsTriangulateHandles;

    void Update()
    {
        //debug
        // UpdateMesh(new int3(0, 0, 0), _gridSize);
    }

    void Start()
    {
        AllocateContainers();

        _cubesGrid.Create(_cellSize, _gridSize);

        _meshFilter = GetComponent<MeshFilter>();
        UpdateMesh(new int3(0, 0, 0), _gridSize);
        var _boxCollider = gameObject.AddComponent<BoxCollider>();
        _boxCollider.size = new Vector3(_gridSize.x * _cellSize, _gridSize.y * _cellSize, _gridSize.z * _cellSize);
        _boxCollider.center = new Vector3(_gridSize.x * _cellSize / 2, _gridSize.y * _cellSize / 2, _gridSize.z * _cellSize / 2);
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
    }

    private void DisposeContainers()
    {
        _jobsHandles.Dispose();
        _jobsCutHandles.Dispose();
        _jobsPullHandles.Dispose();
        _jobsTriangulateHandles.Dispose();
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
        
        _cubesGrid.GetMesh(_meshFilter, _jobsHandles);
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

    public struct MeshGridData : System.IDisposable
    {
        public float cellSize;
        public int3 gridSize;

        private Mesh _mesh;

        VertexAttributeDescriptor[] _layout;


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
                if (indexes[i] >= 0)
                    indexesList.Add(indexes[i]);
            }
        }
    }

    #endregion
}
