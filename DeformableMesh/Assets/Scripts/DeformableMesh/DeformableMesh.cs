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

    void Start()
    {
        _cubesGrid.Create(_cellSize, _gridSize);

        _meshFilter = GetComponent<MeshFilter>();
        _meshFilter.mesh = GenerateCube();
        var _boxCollider = gameObject.AddComponent<BoxCollider>();
        _boxCollider.size = new Vector3(_gridSize.x * _cellSize, _gridSize.y * _cellSize, _gridSize.z * _cellSize);
    }

    void OnDestroy()
    {
        _cubesGrid.Dispose();
    }

    private Mesh GenerateCube()
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

        //create surface cells
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                for (int z = 0; z < _gridSize.z; z++)
                {
                    // CreateSurfaceCell(new int3(x, y, z));
                    _cubesGrid.TriangulateCube(new int3(x, y, z));
                }
            }
        }

        return _cubesGrid.GetMesh();
    }

    private void UpdateMesh()
    {
        //re-create surface cells
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                for (int z = 0; z < _gridSize.z; z++)
                {
                    // CreateSurfaceCell(new int3(x, y, z));
                    _cubesGrid.TriangulateCube(new int3(x, y, z));
                }
            }
        }
        _meshFilter.mesh = _cubesGrid.GetMesh();
    }

    private void CreateSurfaceCell(int3 _cellId)
    {
        //on x
        if (_cellId.x == 0)
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(4);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(5);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(1);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(4);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(1);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(0);
        }
        if (_cellId.x == (_gridSize.x - 1))
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(3);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(2);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(6);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(3);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(6);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(7);
        }
        //on y
        if (_cellId.y == 0)
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(4);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(0);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(3);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(4);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(3);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(7);
        }
        if (_cellId.y == (_gridSize.y - 1))
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(1);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(5);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(6);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(1);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(6);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(2);
        }
        //on z
        if (_cellId.z == 0)
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(0);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(1);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(2);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(0);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(2);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(3);
        }
        if (_cellId.z == (_gridSize.z - 1))
        {
            if (!_cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].vertexIndexes.IsCreated)
                _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].Create(_cellId);
            //add surface
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(7);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(6);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(5);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(7);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(5);
            _cubesGrid.grid[_cellId.x, _cellId.y, _cellId.z].triangles.Add(4);
        }
    }

    public void CutSphere(float3 sphereCenter, float sphereRadius)
    {
        //find cubes to cut
        int _cellsCount = (int)(sphereRadius / _cellSize + 1);
        int3 _from, _to;
        int3 _cellId = (int3)((sphereCenter - (float3)transform.position) / _cellSize) + (_gridSize / 2);
        _from = math.clamp((_cellId - new int3(_cellsCount, _cellsCount, _cellsCount)), new int3(0, 0, 0), _gridSize);
        _to = math.clamp((_cellId + new int3(_cellsCount, _cellsCount, _cellsCount)), new int3(0, 0, 0), _gridSize);
        //iterate in radius

        //iterate in active points
        for (int x = 0; x < (_cubesGrid.gridSize.x + 1); x++)
        {
            for (int y = 0; y < (_cubesGrid.gridSize.y + 1); y++)
            {
                for (int z = 0; z < (_cubesGrid.gridSize.z + 1); z++)
                {
                    if (_cubesGrid.points[x, y][z] > 0)
                    {
                        float3 _toPoint = _cubesGrid.GetPointPosition(new int3(x, y, z)) - sphereCenter;
                        _toPoint *= _toPoint;
                        float _dist = math.sqrt(_toPoint.x + _toPoint.y + _toPoint.z);
                        _dist -= sphereRadius;
                        _dist = _cellSize + _dist;
                        _dist = _dist / _cellSize;
                        float _value = math.clamp(_dist, 0, 1);
                        if (_dist < _cubesGrid.points[x, y][z])
                            _cubesGrid.points[x, y][z] = _value;
                    }
                }
            }
        }

        UpdateMesh();
    }

    public struct CubesGrid : System.IDisposable
    {
        public float cellSize;
        public int3 gridSize;

        public NativeArray<float>[,] points;
        public Cell[,,] grid;

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
        }

        public void Dispose()
        {
            foreach (var array in this.points)
            {
                array.Dispose();
            }
            foreach (var cell in this.grid)
            {
                cell.Dispose();
            }
        }

        public Mesh GetMesh()
        {
            Mesh _mesh = new Mesh();

            NativeHashMap<int3, int> _pointsToMeshVertices = new NativeHashMap<int3, int>(((gridSize.x + 1) * (gridSize.y + 1) * gridSize.z + 1), Allocator.Temp);
            List<Vector3> _vertices = new List<Vector3>();
            //add active vertices
            //add active points
            for (int x = 0; x < (gridSize.x + 1); x++)
            {
                for (int y = 0; y < (gridSize.y + 1); y++)
                {
                    for (int z = 0; z < (gridSize.z + 1); z++)
                    {
                        //check if points is active
                        float _pointValue = points[x, y][z];
                        if (_pointValue > 0)
                        {
                            int3 _pointId = new int3(x, y, z);
                            float3 _pointPosition = GetPointPosition(_pointId);
                            //pull point to connected points
                            if (PullPointToConnected(ref _pointPosition, _pointId, _pointValue)) //do not add point if is is connected to 6 others => internal point
                            {
                                //add to vertices
                                _vertices.Add(_pointPosition);
                                //add to hash-map
                                _pointsToMeshVertices.TryAdd(_pointId, _vertices.Count - 1);
                            }
                        }
                    }
                }
            }
            List<int> _triangles = new List<int>();
            //iterate through cells
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    for (int z = 0; z < gridSize.z; z++)
                    {
                        //check if created
                        Cell _cell = grid[x, y, z];
                        if (_cell.triangles.IsCreated)
                        {
                            foreach (var index in _cell.triangles)
                            {
                                //internal cell index -> global point index -> vertex index
                                _triangles.Add(_pointsToMeshVertices[_cell.vertexIndexes[index]]);
                            }
                        }
                    }
                }
            }

            if (_vertices.Count > 65535)
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            _mesh.vertices = _vertices.ToArray();
            _mesh.triangles = _triangles.ToArray();
            _mesh.RecalculateNormals();

            _pointsToMeshVertices.Dispose();

            return _mesh;
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
    
        public void TriangulateCube(int3 index)
        {
            //check if already created
            if (!grid[index.x, index.y, index.z].vertexIndexes.IsCreated)
            {
                grid[index.x, index.y, index.z].Create(index);
            }
            else
            {
                grid[index.x, index.y, index.z].triangles.Clear();
            }
            //check faces visibility
            NativeArray<int> _faceIndexes = new NativeArray<int>(4, Allocator.Temp);
            NativeArray<int> _mirrorFaceIndexes = new NativeArray<int>(4, Allocator.Temp);
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
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
                var _triangles = TriangulateFace(_faceIndexes, _mirrorFaceIndexes, index);
                grid[index.x, index.y, index.z].triangles.AddRange(_triangles);
                _triangles.Dispose();
            }

            _faceIndexes.Dispose();
            _mirrorFaceIndexes.Dispose();
        }

        private bool CheckFaceVisible(NativeArray<int> faceIndexes, int3 faceNormal, int3 cellId)
        {
            int _fullCount = 0;
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
                if (points[_pointId.x, _pointId.y][_pointId.z] > 0)
                {
                    _fullCount++;
                    if (_fullCount > 3)
                        return false;
                }
            }

            return true;
        }
    
        private NativeList<int> TriangulateFace(NativeArray<int> faceIndexes, NativeArray<int> mirrorFaceIndexes, int3 cellId)
        {
            NativeList<int> _indexes = new NativeList<int>(6, Allocator.Temp);
            NativeArray<bool> _pointStates = new NativeArray<bool>(4, Allocator.Temp);

            //check face points
            for (int i = 0; i < 4; i++)
            {
                var _pointId = grid[cellId.x, cellId.y, cellId.z].vertexIndexes[faceIndexes[i]];
                var _pointValue = points[_pointId.x, _pointId.y][_pointId.z];

                _pointStates[i] = true;

                if (_pointValue <= 0)
                {
                    _pointId = grid[cellId.x, cellId.y, cellId.z].vertexIndexes[mirrorFaceIndexes[i]];
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
                        _indexes.Add(faceIndexes[0]);
                        _indexes.Add(faceIndexes[1]);
                        _indexes.Add(faceIndexes[2]);
                        _oneFound = true;
                    }
                }
                //try take second triangle
                if (_pointStates[2])
                {
                    if (_pointStates[3])
                    {
                        _indexes.Add(faceIndexes[0]);
                        _indexes.Add(faceIndexes[2]);
                        _indexes.Add(faceIndexes[3]);
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
                            _indexes.Add(faceIndexes[0]);
                            _indexes.Add(faceIndexes[1]);
                            _indexes.Add(faceIndexes[3]);
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
                            _indexes.Add(faceIndexes[1]);
                            _indexes.Add(faceIndexes[2]);
                            _indexes.Add(faceIndexes[3]);
                        }
                    }
                    //only one triangle can be built in that situation
                }
            }

            _pointStates.Dispose();

            return _indexes;
        }
    }

    public struct Cell : System.IDisposable
    {
        public NativeList<int> triangles;

        public NativeArray<int3> vertexIndexes;

        public void Create(int3 index)
        {
            triangles = new NativeList<int>(36, Allocator.Persistent);
            vertexIndexes = new NativeArray<int3>(8, Allocator.Persistent);

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

        public void Dispose()
        {
            if (triangles.IsCreated)
                triangles.Dispose();
            if (vertexIndexes.IsCreated)
                vertexIndexes.Dispose();
        }
    }

    public enum CellState
    {
        Whole,
        Cut,
        Disabled
    }
}
