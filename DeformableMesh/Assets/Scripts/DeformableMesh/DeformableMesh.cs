using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;

public class DeformableMesh : MonoBehaviour
{
    //
    [SerializeField] private float _cellSize;
    [SerializeField] private int3 _gridSize;

    private MeshFilter _meshFilter;

    private CubesGrid _cubesGrid;

    //for jobs
    private NativeList<JobHandle> _cutJobsHandles;

    void Start()
    {
        AllocateContainers();

        _cubesGrid.Create(_cellSize, _gridSize);

        _meshFilter = GetComponent<MeshFilter>();
        GenerateCube();
        UpdateMesh(new int3(0, 0, 0), _gridSize);
        var _boxCollider = gameObject.AddComponent<BoxCollider>();
        _boxCollider.size = new Vector3(_gridSize.x * _cellSize, _gridSize.y * _cellSize, _gridSize.z * _cellSize);
    }

    void OnDestroy()
    {
        _cubesGrid.Dispose();
        Disposecontainers();
    }

    private void AllocateContainers()
    {
        _cutJobsHandles = new NativeList<JobHandle>(Allocator.Persistent);
    }

    private void Disposecontainers()
    {
        _cutJobsHandles.Dispose();
    }

    private void GenerateCube()
    {
        //set all points active
        for (int x = 0; x < (_gridSize.x + 1); x++)
        {
            for (int y = 0; y < (_gridSize.y + 1); y++)
            {
                for (int z = 0; z < (_gridSize.z + 1); z++)
                {
                    _cubesGrid.points[x, y][z] = 1;
                }
            }
        }
    }

    private void UpdateMesh(int3 fromIds, int3 toIds)
    {
        int _counter = 0;
        //re-create surface cells
        for (int x = fromIds.x; x < toIds.x; x++)
        {
            for (int y = fromIds.y; y < toIds.y; y++)
            {
                for (int z = fromIds.z; z < toIds.z; z++)
                {
                    _cubesGrid.TriangulateCube(new int3(x, y, z));
                    _counter++;
                }
            }
        }
        _cubesGrid.PullVerticesInRange(fromIds, toIds);
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
            sphereCenter = sphereCenter,
            sphereRadius = sphereRadius,
            cellSize = _cellSize
        };
        //iterate in active points
        for (int x = 0; x < (_cubesGrid.gridSize.x + 1); x++)
        {
            for (int y = 0; y < (_cubesGrid.gridSize.y + 1); y++)
            {
                //code on jobs
                _cutJob.points = _cubesGrid.points[x, y];
                _cutJob.x = x;
                _cutJob.y = y;
                var _handle = _cutJob.ScheduleBatch((_gridSize.z + 1), 0);
                _cutJobsHandles.Add(_handle);
            }
        }

        //complete jobs
        JobHandle.CompleteAll(_cutJobsHandles);
        _cutJobsHandles.Clear();

        UpdateMesh(_from, _to);
    }

    public struct CubesGrid : System.IDisposable
    {
        public float cellSize;
        public int3 gridSize;

        public NativeArray<float>[,] points;
        public Cell[,,] grid;

        private Vector3[] _vertices;
        private List<int> _triangles;
        private Mesh _mesh;

        public void Create(float cellSize, int3 gridSize)
        {
            this.cellSize = cellSize;
            this.gridSize = gridSize;

            //create all points
            points = new NativeArray<float>[gridSize.x + 1, gridSize.y + 1];
            for (int x = 0; x < (gridSize.x + 1); x++)
            {
                for (int y = 0; y < (gridSize.y + 1); y++)
                {
                    points[x, y] = new NativeArray<float>(gridSize.z + 1, Allocator.Persistent);
                }
            }
            //create grid
            grid = new Cell[gridSize.x, gridSize.y, gridSize.z];

            StorePointsAndVertices();

            _faceIndexes = new int[8];
            _mirrorFaceIndexes = new int[8];
            _pointStates = new bool[4];
        }

        public void Dispose()
        {
            foreach (var array in this.points)
            {
                array.Dispose();
            }
        }

        private void StorePointsAndVertices()
        {
            int _size = (gridSize.x + 1) * (gridSize.y + 1) * (gridSize.z + 1);
            _vertices = new Vector3[_size];
            _triangles = new List<int>();

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
                        _vertices[_index] = _pointPosition;
                        //add to hash-map
                        _index++;
                    }
                }
            }
            _mesh = new Mesh();
            if (_vertices.Length > 65535)
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            _mesh.vertices = _vertices;
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
            _triangles.Clear();
            //iterate through cells
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    for (int z = 0; z < gridSize.z; z++)
                    {
                        //check if created
                        var _cellTriangles = grid[x, y, z].triangles;
                        if (_cellTriangles != null)
                        {
                            _triangles.AddRange(_cellTriangles);
                        }
                    }
                }
            }

            _mesh.vertices = _vertices;
            _mesh.SetIndices(_triangles, MeshTopology.Triangles, 0);
            _mesh.RecalculateNormals();

            if (filter.sharedMesh == null)
                filter.sharedMesh = _mesh;
        }
        
        [BurstCompile]
        private int GetPointToVertexIndex(int3 pointId)
        {
            return (pointId.x * (gridSize.y + 1) * (gridSize.z + 1)) + (pointId.y * (gridSize.z + 1)) + pointId.z;
        }

        [BurstCompile]
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

    #region Jobs

    [BurstCompile]
    private struct CutSphereJob : IJobParallelForBatch
    {
        public NativeArray<float> points;

        [ReadOnly] public float3 sphereCenter;
        [ReadOnly] public int x, y;
        [ReadOnly] public float cellSize, sphereRadius;

        public void Execute(int startIndex, int count)
        {
            for (int z = startIndex; z < (startIndex + count); z++)
            {
                if (points[z] > 0)
                {
                    float3 _toPoint = GetPointPosition(new int3(x, y, z), cellSize) - sphereCenter;
                    _toPoint *= _toPoint; //square
                    float _dist = math.sqrt(_toPoint.x + _toPoint.y + _toPoint.z);
                    _dist -= sphereRadius;
                    _dist = cellSize + _dist;
                    _dist = _dist / cellSize;
                    float _value = math.clamp(_dist, 0, 1);
                    if (_value < 0.5f)
                        _value = 0;
                    if (_dist < points[z])
                        points[z] = _value;
                }
            }
        }

        private float3 GetPointPosition(int3 pointId, float cellSize)
        {
            return new float3((cellSize * pointId.x), (cellSize * pointId.y), (cellSize * pointId.z));
        }
    }

    #endregion
}
