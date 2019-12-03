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

    void Start()
    {
        AllocateContainers();

        _cubesGrid.Create(_cellSize, _gridSize);

        _meshFilter = GetComponent<MeshFilter>();
        UpdateMesh(new int3(0, 0, 0), _gridSize);
        var _boxCollider = gameObject.AddComponent<BoxCollider>();
        _boxCollider.size = new Vector3(_gridSize.x * _cellSize, _gridSize.y * _cellSize, _gridSize.z * _cellSize);
    }

    void OnDestroy()
    {
        _cubesGrid.Dispose();
        DisposeContainers();
    }

    private void AllocateContainers()
    {
        _jobsHandles = new NativeList<JobHandle>(Allocator.Persistent);
    }

    private void DisposeContainers()
    {
        _jobsHandles.Dispose();
    }

    private void UpdateMesh(int3 fromIds, int3 toIds)
    {
        _jobsHandles.Clear();
        //re-create surface cells
        TriangulateCubeJob _triangulateJob = new TriangulateCubeJob
        {
            points = _cubesGrid._pointsGrid.points,
            pointsGridSize = _cubesGrid._pointsGrid.size,
            indexes = _cubesGrid._cellsGrid.indexes
        };
        for (int x = fromIds.x; x < toIds.x; x++)
        {
            for (int y = fromIds.y; y < toIds.y; y++)
            {
                for (int z = fromIds.z; z < toIds.z; z++)
                {
                    _triangulateJob.cellId = new int3(x, y, z);
                    _triangulateJob.startIndex = _cubesGrid._cellsGrid.GetCellStartIndex(_triangulateJob.cellId);
                    var _handle = _triangulateJob.Schedule();
                    _jobsHandles.Add(_handle);
                }
            }
        }
        JobHandle.CompleteAll(_jobsHandles);
        // _cubesGrid.PullVerticesInRange(fromIds, toIds);
        _cubesGrid.GetMesh(_meshFilter);
    }

    public void CutSphere(float3 sphereCenter, float sphereRadius)
    {
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
            zStart = _from.z
        };
        //iterate in active points
        for (int x = _from.x; x < (_to.x + 1); x++)
        {
            for (int y = _from.y; y < (_to.y + 1); y++)
            {
                //code on jobs
                _cutJob.x = x;
                _cutJob.y = y;
                _cutJob.startIndex = (x * (_gridSize.y + 1) * (_gridSize.z + 1)) + (y * (_gridSize.z + 1)) + _from.z;
                var _count = ((_to.z + 1) - _from.z);
                var _handle = _cutJob.Schedule(_gridSize.z + 1, _gridSize.z + 1);
                _jobsHandles.Add(_handle);
            }
        }

        //complete jobs
        JobHandle.CompleteAll(_jobsHandles);
        _jobsHandles.Clear();

        UpdateMesh(_from, _to);
    }

    public struct MeshGridData : System.IDisposable
    {
        public float cellSize;
        public int3 gridSize;

        public NativeArray<float>[,] points;
        public Cell[,,] grid;

        private Vector3[] _vertices;
        private List<int> _triangles;
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
            _indexesList = new NativeList<int>(_cellsGrid.indexes.Length, Allocator.Persistent);

            StorePointsAndVertices();
        }

        public void Dispose()
        {
            _pointsGrid.Dispose();
            _cellsGrid.Dispose();
            _verticesArray.Dispose();
            _indexesList.Dispose();
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
            // if (_verticesArray.Length > 65535)
            //     _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            _layout = new VertexAttributeDescriptor[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
            };
            _mesh.SetVertexBufferParams(_verticesArray.Length, _layout);
            _mesh.SetVertexBufferData(_verticesArray, 0, 0, _verticesArray.Length);
        }

        public void PullVerticesInRange(int3 fromIds, int3 toIds)
        {
            toIds += new int3(1, 1, 1);
            for (int x = fromIds.x; x < toIds.x; x++)
            {
                for (int y = fromIds.y; y < toIds.y; y++)
                {
                    for (int z = fromIds.z; z < toIds.z; z++)
                    {
                        int3 _pointId = new int3(x, y, z);
                        float3 _pointPos = GetPointPosition(_pointId);
                        float _pointValue = points[x, y][z];
                        if (PullPointToConnected(ref _pointPos, _pointId, _pointValue))
                        {
                            _vertices[GetPointToVertexIndex(_pointId)] = _pointPos;
                        }
                    }
                }
            }
        }

        public void GetMesh(MeshFilter filter)
        {
            _indexesList.Clear();

            CopyIndexesToListJob _copyIndexesJob = new CopyIndexesToListJob
            {
                indexes = _cellsGrid.indexes,
                indexesList = _indexesList
            };

            _copyIndexesJob.Schedule().Complete();

            _mesh.SetVertexBufferParams(_verticesArray.Length, _layout);
            _mesh.SetVertexBufferData(_verticesArray, 0, 0, _verticesArray.Length);
            _mesh.SetIndexBufferParams(_indexesList.Length, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(_indexesList.ToArray(), 0, 0, _indexesList.Length);
            SubMeshDescriptor _smd = new SubMeshDescriptor(0, _indexesList.Length);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, _smd);
            _mesh.RecalculateNormals();

            if (filter.sharedMesh == null)
                filter.sharedMesh = _mesh;
        }
        
        private int GetPointToVertexIndex(int3 pointId)
        {
            return (pointId.x * (gridSize.y + 1) * (gridSize.z + 1)) + (pointId.y * (gridSize.z + 1)) + pointId.z;
        }

        public float3 GetPointPosition(int3 pointId)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }

        private bool PullPointToConnected(ref float3 pointPosition, int3 pointId, float pointValue)
        {
            int _connectedCount = 0;
            float3 _pullVector;
            float _pullValue = 1 - pointValue;

            // return true;
            //pull on x
            if (pointId.x >= 0 && pointId.x <= gridSize.x)
            {
                if (pointId.x == 0)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x + 1, pointId.y, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (pointId.x == gridSize.x)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x - 1, pointId.y, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                } 
                else if (points[pointId.x - 1, pointId.y][pointId.z] > 0 && points[pointId.x + 1, pointId.y][pointId.z] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x - 1, pointId.y, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (points[pointId.x + 1, pointId.y][pointId.z] > 0 && points[pointId.x - 1, pointId.y][pointId.z] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x + 1, pointId.y, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
            }
            //pull on y
            if (pointId.y >= 0 && pointId.y <= gridSize.y)
            {
                if (pointId.y == 0)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y + 1, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (pointId.y == gridSize.y)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y - 1, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                } 
                else if (points[pointId.x, pointId.y - 1][pointId.z] > 0 && points[pointId.x, pointId.y + 1][pointId.z] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y - 1, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (points[pointId.x, pointId.y + 1][pointId.z] > 0 && points[pointId.x, pointId.y - 1][pointId.z] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y + 1, pointId.z)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
            }
            //pull on z
            if (pointId.z >= 0 && pointId.z <= gridSize.z)
            {
                if (pointId.z == 0)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y, pointId.z + 1)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (pointId.z == gridSize.z)
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y, pointId.z - 1)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                } 
                else if (points[pointId.x, pointId.y][pointId.z - 1] > 0 && points[pointId.x, pointId.y][pointId.z + 1] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y, pointId.z - 1)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
                else if (points[pointId.x, pointId.y][pointId.z + 1] > 0 && points[pointId.x, pointId.y][pointId.z - 1] <= 0)//if connected is active point
                {
                    _connectedCount++;
                    _pullVector = GetPointPosition(new int3(pointId.x, pointId.y, pointId.z + 1)) - pointPosition;
                    pointPosition += (_pullVector * _pullValue);
                }
            }
            if (_connectedCount == 6)
                return false;
            else
                return true;
        }
    
        private int[] _faceIndexes, _mirrorFaceIndexes;

        public void TriangulateCube(int3 index)
        {
            //check if already created
            if (grid[index.x, index.y, index.z].triangles == null)
            {
                grid[index.x, index.y, index.z].Create(index);
            }
            else
            {
                grid[index.x, index.y, index.z].triangles.Clear();
            }
            //check faces visibility
            int3 _faceNormal;

            //check front face
            _faceIndexes[0] = 0;
            _faceIndexes[1] = 1;
            _faceIndexes[2] = 2;
            _faceIndexes[3] = 3;
            //
            _mirrorFaceIndexes[0] = 4;
            _mirrorFaceIndexes[1] = 5;
            _mirrorFaceIndexes[2] = 6;
            _mirrorFaceIndexes[3] = 7;
            //
            _faceNormal = new int3(0, 0, -1);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }

            //check back face
            _faceIndexes[0] = 7;
            _faceIndexes[1] = 6;
            _faceIndexes[2] = 5;
            _faceIndexes[3] = 4;
            //
            _mirrorFaceIndexes[0] = 3;
            _mirrorFaceIndexes[1] = 2;
            _mirrorFaceIndexes[2] = 1;
            _mirrorFaceIndexes[3] = 0;
            //
            _faceNormal = new int3(0, 0, 1);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }

            //check left face
            _faceIndexes[0] = 4;
            _faceIndexes[1] = 5;
            _faceIndexes[2] = 1;
            _faceIndexes[3] = 0;
            //
            _mirrorFaceIndexes[0] = 7;
            _mirrorFaceIndexes[1] = 6;
            _mirrorFaceIndexes[2] = 2;
            _mirrorFaceIndexes[3] = 3;
            //
            _faceNormal = new int3(-1, 0, 0);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }

            //check right face
            _faceIndexes[0] = 3;
            _faceIndexes[1] = 2;
            _faceIndexes[2] = 6;
            _faceIndexes[3] = 7;
            //
            _mirrorFaceIndexes[0] = 0;
            _mirrorFaceIndexes[1] = 1;
            _mirrorFaceIndexes[2] = 5;
            _mirrorFaceIndexes[3] = 4;
            //
            _faceNormal = new int3(1, 0, 0);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }

            //check top face
            _faceIndexes[0] = 1;
            _faceIndexes[1] = 5;
            _faceIndexes[2] = 6;
            _faceIndexes[3] = 2;
            //
            _mirrorFaceIndexes[0] = 0;
            _mirrorFaceIndexes[1] = 4;
            _mirrorFaceIndexes[2] = 7;
            _mirrorFaceIndexes[3] = 3;
            //
            _faceNormal = new int3(0, 1, 0);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }

            //check bottom face
            _faceIndexes[0] = 3;
            _faceIndexes[1] = 7;
            _faceIndexes[2] = 4;
            _faceIndexes[3] = 0;
            //
            _mirrorFaceIndexes[0] = 2;
            _mirrorFaceIndexes[1] = 6;
            _mirrorFaceIndexes[2] = 5;
            _mirrorFaceIndexes[3] = 1;
            //
            _faceNormal = new int3(0, -1, 0);
            if (CheckFaceVisible(_faceIndexes, _faceNormal, index))
            {
                TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
            }
        }

        private bool[] _pointStates;

        private bool CheckFaceVisible(int[] faceIndexes, int3 faceNormal, int3 cellId)
        {
            for (int i = 0; i < 4; i++)
            {
                var _pointId = grid[cellId.x, cellId.y, cellId.z].vertexIndexes[faceIndexes[i]] + faceNormal;
                //check boundaries
                if (faceNormal.x != 0)
                {
                    if (_pointId.x < 0 || _pointId.x > gridSize.x)//always visible if on edge
                        return true;
                }
                if (faceNormal.y != 0)
                {
                    if (_pointId.y < 0 || _pointId.y > gridSize.y)//always visible if on edge
                        return true;
                }
                if (faceNormal.z != 0)
                {
                    if (_pointId.z < 0 || _pointId.z > gridSize.z)//always visible if on edge
                        return true;
                }
                //check visible
                if (points[_pointId.x, _pointId.y][_pointId.z] <= 0)
                {
                    return true;
                }
            }

            return false;
        }
    
        private void TriangulateFace(int[] faceIndexes, int[] mirrorFaceIndexes, int3 cellId)
        {
            var _cubeTriangles = grid[cellId.x, cellId.y, cellId.z].triangles;
            var _cubePoints = grid[cellId.x, cellId.y, cellId.z].vertexIndexes;

            //check face points
            for (int i = 0; i < 4; i++)
            {
                var _pointId = _cubePoints[faceIndexes[i]];
                var _pointValue = points[_pointId.x, _pointId.y][_pointId.z];

                _pointStates[i] = true;

                if (_pointValue <= 0)
                {
                    _pointId = _cubePoints[mirrorFaceIndexes[i]];
                    _pointValue = points[_pointId.x, _pointId.y][_pointId.z];

                    if (_pointValue > 0)
                    {
                        faceIndexes[i] = mirrorFaceIndexes[i];
                    }
                    else
                    {
                        _pointStates[i] = false;
                    }
                }
            }

            //try take first triangle
            if (_pointStates[0])
            {
                bool _oneFound = false;
                if (_pointStates[1])
                {
                    if (_pointStates[2])
                    {
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[0]]));
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[1]]));
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[2]]));
                        _oneFound = true;
                    }
                }
                //try take second triangle
                if (_pointStates[2])
                {
                    if (_pointStates[3])
                    {
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[0]]));
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[2]]));
                        _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[3]]));
                        _oneFound = true;
                    }
                }
                //try different pattern
                if (!_oneFound)
                {
                    if (_pointStates[1])
                    {
                        if (_pointStates[3])
                        {
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[0]]));
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[1]]));
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[3]]));
                        }
                    }
                }
            }
            else //try start from second point
            {
                if (_pointStates[1])
                {
                    if (_pointStates[2])
                    {
                        if (_pointStates[3])
                        {
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[1]]));
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[2]]));
                            _cubeTriangles.Add(GetPointToVertexIndex(_cubePoints[faceIndexes[3]]));
                        }
                    }
                    //only one triangle can be built in that situation
                }
            }
        }
    }

    public struct Cell
    {
        public List<int> triangles;

        public int3[] vertexIndexes;

        public void Create(int3 index)
        {
            triangles = new List<int>(36);
            vertexIndexes = new int3[8];

            //associate vertices and points
            vertexIndexes[0] = index;
            vertexIndexes[1] = new int3(index.x, index.y + 1, index.z);
            vertexIndexes[2] = new int3(index.x + 1, index.y + 1, index.z);
            vertexIndexes[3] = new int3(index.x + 1, index.y, index.z);
            vertexIndexes[4] = new int3(index.x, index.y, index.z + 1);
            vertexIndexes[5] = new int3(index.x, index.y + 1, index.z + 1);
            vertexIndexes[6] = new int3(index.x + 1, index.y + 1, index.z + 1);
            vertexIndexes[7] = new int3(index.x + 1, index.y, index.z + 1);
        }
    }

    [BurstCompile]
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

    [BurstCompile]
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

    [BurstCompile]
    public struct Cube
    {
        public int3 zero, one, two, three, four, five, six, seven;
        public bool isZero, isOne, isTwo, isThree, isFour, isFive, isSix, isSeven;
    }

    [BurstCompile]
    public struct Face
    {
        public int3 zero, one, two, three;
        public bool isZero, isOne, isTwo, isThree;
    }

    #region Jobs

    [BurstCompile]
    private struct CutSphereJob : IJobParallelFor
    {
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] 
        public NativeArray<byte> points;

        [ReadOnly] public float3 sphereCenter;
        [ReadOnly] public float cellSize, sphereRadius;
        [ReadOnly] public int x, y, zStart;
        [ReadOnly] public int startIndex;

        public void Execute(int index)
        {
            var _pointId = index + startIndex;

            if (points[_pointId] > 0)
            {
                float3 _toPoint = GetPointPosition(new int3(x, y, (zStart + index)), cellSize) - sphereCenter;
                _toPoint *= _toPoint; //square
                float _dist = math.sqrt(_toPoint.x + _toPoint.y + _toPoint.z);
                _dist -= sphereRadius;
                _dist = cellSize + _dist;
                _dist = _dist / cellSize;
                float _value = math.clamp(_dist, 0, 1);

                byte _pValue = (byte)math.lerp(0, 255, _value);
                if (_pValue < 200)
                    _pValue = 0;
                if (_dist < points[_pointId])
                    points[_pointId] = _pValue;
            }
        }

        private float3 GetPointPosition(int3 pointId, float cellSize)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }
    }

    [BurstCompile]
    private struct TriangulateCubeJob : IJob
    {
        [ReadOnly] public NativeArray<byte> points;
        [ReadOnly] public int3 pointsGridSize;
        [ReadOnly] public int3 cellId;
        [ReadOnly] public int startIndex;

        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
        [WriteOnly] public NativeArray<int> indexes;

        public void Execute()
        {
            //get cell points
            Cube _cube = new Cube
            {
                zero = cellId,
                one = new int3(cellId.x, cellId.y + 1, cellId.z),
                two = new int3(cellId.x + 1, cellId.y + 1, cellId.z),
                three = new int3(cellId.x + 1, cellId.y, cellId.z),
                four = new int3(cellId.x, cellId.y, cellId.z + 1),
                five = new int3(cellId.x, cellId.y + 1, cellId.z + 1),
                six = new int3(cellId.x + 1, cellId.y + 1, cellId.z + 1),
                seven = new int3(cellId.x + 1, cellId.y, cellId.z + 1)
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
