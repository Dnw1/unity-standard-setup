using System.Collections.Generic;
using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Generic object pool for Unity Components to reduce GC spikes from frequent instantiation/destruction.
    /// Reuses objects instead of creating/destroying them, improving performance.
    /// </summary>
    /// <typeparam name="T">Component type to pool (must inherit from Component)</typeparam>
    public class ObjectPool<T> where T : Component
    {
        private Queue<T> _pool = new Queue<T>();
        private T _prefab;
        private Transform _parent;
        private int _initialSize;
        private int _maxSize;
        private int _currentSize;

        /// <summary>
        /// Creates a new object pool for the specified prefab.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate (must have component T)</param>
        /// <param name="parent">Parent transform for pooled objects</param>
        /// <param name="initialSize">Number of objects to pre-populate the pool (default: 10)</param>
        /// <param name="maxSize">Maximum pool size (0 = unlimited, default: 0)</param>
        public ObjectPool(T prefab, Transform parent, int initialSize = 10, int maxSize = 0)
        {
            if (prefab == null)
            {
                Debug.LogError("[ObjectPool] Cannot create pool: prefab is null");
                return;
            }

            _prefab = prefab;
            _parent = parent;
            _initialSize = initialSize;
            _maxSize = maxSize;
            _currentSize = 0;

            // Pre-populate pool
            for (int i = 0; i < initialSize; i++)
            {
                var obj = Object.Instantiate(_prefab, _parent);
                obj.gameObject.SetActive(false);
                _pool.Enqueue(obj);
                _currentSize++;
            }

            Debug.Log($"[ObjectPool] Created pool for {typeof(T).Name} with {initialSize} initial objects");
        }

        /// <summary>
        /// Gets an object from the pool. If pool is empty, creates a new instance.
        /// </summary>
        /// <returns>Active object from pool or newly instantiated object</returns>
        public T Get()
        {
            T obj;

            if (_pool.Count > 0)
            {
                // Reuse from pool
                obj = _pool.Dequeue();
                obj.gameObject.SetActive(true);
            }
            else
            {
                // Pool empty, create new instance
                obj = Object.Instantiate(_prefab, _parent);
                obj.gameObject.SetActive(true);
                _currentSize++;
                
                Debug.Log($"[ObjectPool] Pool empty, created new {typeof(T).Name} instance (total: {_currentSize})");
            }

            return obj;
        }

        /// <summary>
        /// Returns an object to the pool. Object will be deactivated and reused later.
        /// </summary>
        /// <param name="obj">Object to return to pool</param>
        public void Return(T obj)
        {
            if (obj == null)
            {
                Debug.LogWarning("[ObjectPool] Cannot return null object to pool");
                return;
            }

            // Check if pool is at max size
            if (_maxSize > 0 && _pool.Count >= _maxSize)
            {
                // Pool is full, destroy the object instead
                Object.Destroy(obj.gameObject);
                _currentSize--;
                Debug.Log($"[ObjectPool] Pool full ({_maxSize}), destroyed {typeof(T).Name} instead of returning");
                return;
            }

            // Return to pool
            obj.gameObject.SetActive(false);
            _pool.Enqueue(obj);
        }

        /// <summary>
        /// Gets the current number of objects in the pool (available for reuse).
        /// </summary>
        /// <returns>Number of objects in pool</returns>
        public int GetPoolSize()
        {
            return _pool.Count;
        }

        /// <summary>
        /// Gets the total number of objects created by this pool (in pool + in use).
        /// </summary>
        /// <returns>Total number of objects created</returns>
        public int GetTotalSize()
        {
            return _currentSize;
        }

        /// <summary>
        /// Clears the pool and destroys all pooled objects.
        /// </summary>
        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var obj = _pool.Dequeue();
                if (obj != null)
                {
                    Object.Destroy(obj.gameObject);
                }
            }
            _currentSize = 0;
            Debug.Log($"[ObjectPool] Cleared pool for {typeof(T).Name}");
        }
    }
}
