using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SceneManagement
{
    [DisallowMultipleComponent]
    public class EnableObjectsSequential : MonoBehaviour
    {
        [SerializeField] private int _enabledPerStep = 5;
        [SerializeField] private float _stepTime = 0f;
        [Header("Automatically selects children if empty")]
        [SerializeField] private List<GameObject> _objects = new List<GameObject>();

        private void Awake()
        {
            if (_objects == null || _objects.Count == 0)
            {
                Transform[] children = new Transform[transform.childCount];
                for (int i = 0; i < children.Length; i++)
                {
                    children[i] = transform.GetChild(i);
                }

                foreach (Transform t in children)
                {
                    if (t == transform) continue;
                    _objects.Add(t.gameObject);
                }
            }

            DisableAll();
            EnableAllSequential();
        }

        private void DisableAll()
        {
            foreach (GameObject obj in _objects)
            {
                obj.SetActive(false);
            }
        }

        private void EnableAllSequential()
        {
            StartCoroutine(EnableSequentialRoutine());
        }

        private IEnumerator EnableSequentialRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(_stepTime);

            int stepCounter = 0;
            for (int i = 0; i < _objects.Count; i++)
            {
                _objects[i].SetActive(true);
                stepCounter++;
                if (stepCounter >= _enabledPerStep)
                {
                    stepCounter = 0;
                    yield return wait;
                }
            }
        }
    }
}