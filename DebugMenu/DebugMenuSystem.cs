using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UI;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DebugMenu
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class DebugCommandAttribute : Attribute
    {
        public string Name { get; private set; }

        public DebugCommandAttribute(string name)
        {
            Name = name;
        }
    }

    [DefaultExecutionOrder(-100)]
    public class DebugMenuSystem : MonoBehaviour
    {
        private class DebugCommandPair
        {
            public object Target;
            public MethodInfo Method;

            public DebugCommandPair(object target, MethodInfo method)
            {
                Target = target;
                Method = method;
            }
        }

        [SerializeField] private KeyCode _toggleKey = KeyCode.Tilde;
        [SerializeField] private CursorLockMode _closedCursorMode = CursorLockMode.Locked;
        [SerializeField] private bool _closedCursorVisible = false;
        [SerializeField] private Transform _panel;
        [SerializeField] private Transform _buttonLayout;
        [SerializeField] private Button _buttonPrefab;
        [SerializeField] private bool _pauseOnOpen = false;
        [SerializeField] private string _openActionMap = "UI";
        [SerializeField] private string _closeActionMap = "Player";

        public static DebugMenuSystem Instance { get; private set; }

        private Dictionary<string, List<DebugCommandPair>> _commands;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("Duplicate DebugMenuSystem found, destroying!");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _commands = new Dictionary<string, List<DebugCommandPair>>();
        }

#if DEBUG_MENU
        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                if (!_panel.gameObject.activeInHierarchy) Open();
                else Close();
            }
        }
#endif

        private void Open()
        {
            _panel.gameObject.SetActive(true);

            foreach (var command in _commands)
            {
                Button button = Instantiate(_buttonPrefab);
                button.GetComponentInChildren<TextMeshProUGUI>().text = command.Key;
                button.transform.SetParent(_buttonLayout);

                foreach (var commandPair in command.Value)
                {
                    button.onClick.AddListener(() => commandPair.Method.Invoke(commandPair.Target, null));
                }
            }

            EventSystem.current.SetSelectedGameObject(null);
            if (_buttonLayout.childCount >= 1) EventSystem.current.SetSelectedGameObject(_buttonLayout.GetChild(0).gameObject);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SwitchActionhMaps(_openActionMap);
            if (_pauseOnOpen) Time.timeScale = 0f;
        }

        private void Close()
        {
            for (int i = 0; i < _buttonLayout.childCount; i++)
            {
                Destroy(_buttonLayout.GetChild(i).gameObject);
            }

            _panel.gameObject.SetActive(false);

            Cursor.lockState = _closedCursorMode;
            Cursor.visible = _closedCursorVisible;

            SwitchActionhMaps(_closeActionMap);
            if (_pauseOnOpen) Time.timeScale = 1f;
        }

        public void SwitchActionhMaps(string newMap)
        {
            if(string.IsNullOrEmpty(newMap)) return;
            PlayerInput[] playerInputs = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
            for (int i = 0; i < playerInputs.Length; i++)
            {
                PlayerInput playerInput = playerInputs[i];
                playerInput.SwitchCurrentActionMap(newMap);
            }
        }

        public void RegisterObject(object component)
        {
#if DEBUG_MENU
            var methods = component.GetType().
                            GetMethods(BindingFlags.Public | BindingFlags.Instance).
                            Where(x => x.GetCustomAttributes(typeof(DebugCommandAttribute), true).Any());

            foreach (var method in methods)
            {
                DebugCommandAttribute att = (DebugCommandAttribute)method.GetCustomAttributes(typeof(DebugCommandAttribute), true).FirstOrDefault();
                if (!_commands.ContainsKey(att.Name)) _commands.Add(att.Name, new List<DebugCommandPair>());
                _commands[att.Name].Add(new DebugCommandPair(component, method));
            }
#endif
        }

        public void DeregisterObject(object component)
        {
#if DEBUG_MENU
            var methods = component.GetType().
                            GetMethods(BindingFlags.Public | BindingFlags.Instance).
                            Where(x => x.GetCustomAttributes(typeof(DebugCommandAttribute), true).Any());

            foreach (var method in methods)
            {
                DebugCommandAttribute att = (DebugCommandAttribute)method.GetCustomAttributes(typeof(DebugCommandAttribute), true).FirstOrDefault();
                if (!_commands.ContainsKey(att.Name)) continue;
                List<DebugCommandPair> pairs = _commands[att.Name];
                DebugCommandPair match = null;
                foreach (var pair in pairs)
                {
                    if (pair.Target == component && pair.Method == method)
                    {
                        match = pair;
                        break;
                    }
                }
                if (match != null) pairs.Remove(match);
            }

            _commands = _commands.Where(x => x.Value.Count() > 0).ToDictionary(x => x.Key, x => x.Value);
#endif
        }
    }
}