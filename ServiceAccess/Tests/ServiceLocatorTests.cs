using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityTools.Services;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTools.Services.Tests
{
    public class ServiceLocatorTests
    {
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.ResetForTests();
            ServiceLocator.LogChanges = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            ServiceLocator.ResetForTests();
        }

        private T Create<T>(string name) where T : ScriptableObject
        {
            T service = ScriptableObject.CreateInstance<T>();
            service.name = name;
            _created.Add(service);
            return service;
        }

        // ---------- Register and get ----------

        [Test]
        public void Register_ThenGet_ReturnsService()
        {
            var audio = Create<TestAudioManager>("Audio");

            ServiceLocator.Register(audio);

            Assert.AreSame(audio, ServiceLocator.Get<TestAudioManager>());
            Assert.IsTrue(ServiceLocator.TryGet(out TestAudioManager found));
            Assert.AreSame(audio, found);
        }

        [Test]
        public void Register_PlainCSharpObject_Works()
        {
            var service = new PlainService();

            ServiceLocator.Register(service);

            Assert.AreSame(service, ServiceLocator.Get<PlainService>());
        }

        [Test]
        public void Register_AsInterface_GetsByInterface()
        {
            var audio = Create<TestAudioManager>("Audio");

            ServiceLocator.Register<ITestSound>(audio);

            Assert.AreSame(audio, ServiceLocator.Get<ITestSound>());
        }

        [Test]
        public void Register_WithRuntimeType_Works()
        {
            var audio = Create<TestAudioManager>("Audio");

            ServiceLocator.Register(typeof(ITestSound), audio);

            Assert.AreSame(audio, ServiceLocator.Get<ITestSound>());
        }

        [Test]
        public void Register_WithTypeTheServiceIsNot_LogsError()
        {
            var score = Create<TestScoreManager>("Score");

            LogAssert.Expect(LogType.Error, new Regex("isn't one"));
            ServiceLocator.Register(typeof(ITestSound), score);

            Assert.IsFalse(ServiceLocator.TryGet(out ITestSound _));
        }

        [Test]
        public void Register_Null_LogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex("null"));
            ServiceLocator.Register<TestAudioManager>(null);
        }

        [Test]
        public void Register_DestroyedObject_LogsError()
        {
            var audio = Create<TestAudioManager>("Audio");
            Object.DestroyImmediate(audio);

            LogAssert.Expect(LogType.Error, new Regex("null"));
            ServiceLocator.Register(audio);
        }

        // ---------- Missing services ----------

        [Test]
        public void Get_Missing_LogsHelpfulError_AndReturnsNull()
        {
            var caller = Create<TestScoreManager>("Caller");

            LogAssert.Expect(LogType.Error, new Regex("no TestAudioManager is registered.*Awake.*Start"));
            Assert.IsNull(ServiceLocator.Get<TestAudioManager>(caller));
        }

        [Test]
        public void TryGet_Missing_ReturnsFalseWithoutLogging()
        {
            Assert.IsFalse(ServiceLocator.TryGet(out TestAudioManager service));
            Assert.IsNull(service);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- Lookup by base type or interface ----------

        [Test]
        public void Get_ByInterface_FindsTheOneServiceThatImplementsIt()
        {
            var audio = Create<TestAudioManager>("Audio");
            ServiceLocator.Register(audio);

            Assert.AreSame(audio, ServiceLocator.Get<ITestSound>());
        }

        [Test]
        public void Get_ByBaseClass_FindsDerivedService()
        {
            var service = new DerivedPlainService();
            ServiceLocator.Register(service);

            Assert.AreSame(service, ServiceLocator.Get<PlainService>());
        }

        [Test]
        public void Get_ExactTypeWins_OverAssignableServices()
        {
            var derived = new DerivedPlainService();
            var exact = new PlainService();
            ServiceLocator.Register(derived);
            ServiceLocator.Register(exact);

            Assert.AreSame(exact, ServiceLocator.Get<PlainService>());
        }

        [Test]
        public void Get_WhenTwoServicesMatch_LogsErrorNamingBoth()
        {
            ServiceLocator.Register(Create<TestAudioManager>("Audio"));
            ServiceLocator.Register(Create<TestScoreManager>("Score"));

            LogAssert.Expect(LogType.Error, new Regex("more than one.*ITestShared.*Audio.*Score"));
            Assert.IsNull(ServiceLocator.Get<ITestShared>());
            Assert.IsFalse(ServiceLocator.TryGet(out ITestShared _));
        }

        [Test]
        public void Get_SameServiceUnderTwoTypes_IsNotAmbiguous()
        {
            var audio = Create<TestAudioManager>("Audio");
            ServiceLocator.Register(audio);
            ServiceLocator.Register<ITestSound>(audio);

            Assert.AreSame(audio, ServiceLocator.Get<ITestShared>());
        }

        // ---------- Duplicates ----------

        [Test]
        public void Register_Twice_SameInstance_IsIgnored()
        {
            var audio = Create<TestAudioManager>("Audio");

            ServiceLocator.Register(audio);
            ServiceLocator.Register(audio);

            Assert.AreSame(audio, ServiceLocator.Get<TestAudioManager>());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Register_SecondLiveInstance_LogsError_AndKeepsFirst()
        {
            var first = Create<TestAudioManager>("First");
            var second = Create<TestAudioManager>("Second");
            ServiceLocator.Register(first);

            LogAssert.Expect(LogType.Error, new Regex("already registered.*First.*Second.*more than one"));
            ServiceLocator.Register(second);

            Assert.AreSame(first, ServiceLocator.Get<TestAudioManager>());
        }

        [Test]
        public void Register_ReplacesDestroyedService_Silently()
        {
            var old = Create<TestAudioManager>("Old");
            ServiceLocator.Register(old);
            Object.DestroyImmediate(old);
            var replacement = Create<TestAudioManager>("New");

            ServiceLocator.Register(replacement);

            Assert.AreSame(replacement, ServiceLocator.Get<TestAudioManager>());
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- Destroyed services ----------

        [Test]
        public void DestroyedService_CountsAsUnregistered()
        {
            var audio = Create<TestAudioManager>("Audio");
            ServiceLocator.Register(audio);

            Object.DestroyImmediate(audio);

            Assert.IsFalse(ServiceLocator.TryGet(out TestAudioManager _));
            Assert.IsFalse(ServiceLocator.TryGet(out ITestSound _));
        }

        [Test]
        public void DestroyedService_DoesNotMakeInterfaceLookupAmbiguous()
        {
            var audio = Create<TestAudioManager>("Audio");
            var score = Create<TestScoreManager>("Score");
            ServiceLocator.Register(audio);
            ServiceLocator.Register(score);

            Object.DestroyImmediate(audio);

            Assert.AreSame(score, ServiceLocator.Get<ITestShared>());
        }

        [Test]
        public void Services_SurviveWhenOtherObjectsAreDestroyed()
        {
            var persistent = Create<TestAudioManager>("Persistent");
            var perScene = Create<TestScoreManager>("PerScene");
            ServiceLocator.Register(persistent);
            ServiceLocator.Register(perScene);

            // stands in for a scene unload destroying the per-scene manager
            Object.DestroyImmediate(perScene);

            Assert.AreSame(persistent, ServiceLocator.Get<TestAudioManager>());
            Assert.IsFalse(ServiceLocator.TryGet(out TestScoreManager _));
        }

        // ---------- Unregister ----------

        [Test]
        public void Unregister_RemovesServiceWhereverRegistered()
        {
            var audio = Create<TestAudioManager>("Audio");
            ServiceLocator.Register(audio);
            ServiceLocator.Register<ITestSound>(audio);

            Assert.IsTrue(ServiceLocator.Unregister(audio));

            Assert.IsFalse(ServiceLocator.TryGet(out TestAudioManager _));
            Assert.IsFalse(ServiceLocator.TryGet(out ITestSound _));
        }

        [Test]
        public void Unregister_DoesNotRemoveAReplacement()
        {
            var old = Create<TestAudioManager>("Old");
            ServiceLocator.Register(old);
            Object.DestroyImmediate(old);
            var replacement = Create<TestAudioManager>("New");
            ServiceLocator.Register(replacement);

            // the old object's OnDestroy runs late
            Assert.IsFalse(ServiceLocator.Unregister(old));

            Assert.AreSame(replacement, ServiceLocator.Get<TestAudioManager>());
        }

        [Test]
        public void Unregister_WithType_OnlyRemovesThatInstance()
        {
            var first = Create<TestAudioManager>("First");
            var other = Create<TestAudioManager>("Other");
            ServiceLocator.Register(first);

            Assert.IsFalse(ServiceLocator.Unregister(typeof(TestAudioManager), other));
            Assert.IsTrue(ServiceLocator.Unregister(typeof(TestAudioManager), first));
            Assert.IsFalse(ServiceLocator.TryGet(out TestAudioManager _));
        }

        [Test]
        public void Unregister_NotRegistered_ReturnsFalseWithoutLogging()
        {
            Assert.IsFalse(ServiceLocator.Unregister(new PlainService()));
            Assert.IsFalse(ServiceLocator.Unregister(null));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- Reset, logging, debug info ----------

        [Test]
        public void Reset_ClearsEverything()
        {
            ServiceLocator.Register(new PlainService());

            ServiceLocator.ResetForTests();

            Assert.IsFalse(ServiceLocator.TryGet(out PlainService _));
        }

        [Test]
        public void LogChanges_LogsRegisterAndUnregister()
        {
            ServiceLocator.LogChanges = true;
            var service = new PlainService();

            LogAssert.Expect(LogType.Log, new Regex("registered PlainService as PlainService"));
            ServiceLocator.Register(service);
            LogAssert.Expect(LogType.Log, new Regex("unregistered PlainService"));
            ServiceLocator.Unregister(service);
        }

        [Test]
        public void CollectDebugInfo_ListsServicesAndMarksDestroyed()
        {
            var audio = Create<TestAudioManager>("Audio");
            ServiceLocator.Register(audio);
            ServiceLocator.Register(new PlainService());
            Object.DestroyImmediate(audio);

            var infos = new List<ServiceLocator.ServiceInfo>();
            ServiceLocator.CollectDebugInfo(infos);

            Assert.AreEqual(2, infos.Count);
            Assert.IsTrue(infos.Find(i => i.RegisteredAs == typeof(TestAudioManager)).IsDestroyed);
            Assert.IsFalse(infos.Find(i => i.RegisteredAs == typeof(PlainService)).IsDestroyed);
        }

        // ---------- Old API ----------

#pragma warning disable CS0618
        [Test]
        public void ObsoleteGlobalServices_ForwardsToTheSameRegistry()
        {
            var audio = Create<TestAudioManager>("Audio");

            ServiceLocator.GlobalServices.Register(audio);

            Assert.AreSame(audio, ServiceLocator.Get<TestAudioManager>());
            Assert.AreSame(audio, ServiceLocator.GlobalServices.Get<TestAudioManager>());
            Assert.IsTrue(ServiceLocator.GlobalServices.TryGet(out TestAudioManager _));
            CollectionAssert.Contains(ServiceLocator.GlobalServices.ServiceNames, "TestAudioManager");

            ServiceLocator.GlobalServices.UnRegister(audio);
            Assert.IsFalse(ServiceLocator.TryGet(out TestAudioManager _));
        }

        [Test]
        public void ObsoleteServiceRegistry_NewInstance_SharesTheRegistry()
        {
            var service = new PlainService();

            new ServiceRegistry(true, false, null).Register(service);

            Assert.AreSame(service, ServiceLocator.Get<PlainService>());
        }
#pragma warning restore CS0618
    }
}
