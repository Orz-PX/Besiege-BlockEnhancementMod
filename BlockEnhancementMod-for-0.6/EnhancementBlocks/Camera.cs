﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Modding;
using Modding.Blocks;
using Modding.Common;
using UnityEngine;

namespace BlockEnhancementMod.Blocks
{
    class CameraScript : EnhancementBlock
    {
        //General setting
        MToggle CameraLookAtToggle;
        public bool cameraLookAtToggled = false;
        private int selfIndex;
        public FixedCameraBlock fixedCamera;
        private Transform smoothLook;
        public FixedCameraController fixedCameraController;
        private Quaternion defaultLocalRotation;
        public float smooth;
        public float smoothLerp;
        private float newCamFOV, orgCamFOV;

        //Track target setting
        MKey LockTargetKey;
        public Transform target;
        private HashSet<Transform> explodedTarget = new HashSet<Transform>();
        public List<KeyCode> lockKeys = new List<KeyCode> { KeyCode.Delete };
        private List<Collider> colliders = new List<Collider>();

        //Pause tracking setting
        MKey PauseTrackingKey;
        public bool pauseTracking = false;
        public List<KeyCode> pauseKeys = new List<KeyCode> { KeyCode.X };

        //Record target related setting
        //MToggle RecordTargetToggle;
        //public bool recordTarget = false;

        //Auto lookat related setting
        MSlider NonCustomModeSmoothSlider;
        MKey AutoLookAtKey;
        private bool firstPersonMode = false;
        public float firstPersonSmooth = 0.25f;
        private float timeOfDestruction = 0f;
        private readonly float targetSwitchDelay = 1.25f;
        public List<KeyCode> activeGuideKeys = new List<KeyCode> { KeyCode.RightShift };
        private float searchAngle = 90;
        private readonly float safetyRadius = 25f;
        private bool autoSearch = true;
        private bool targetAquired = false;
        private bool searchStarted = false;

        protected override void SafeAwake()
        {
            CameraLookAtToggle = AddToggle(LanguageManager.trackTarget, "TrackingCamera", cameraLookAtToggled);
            CameraLookAtToggle.Toggled += (bool value) =>
            {
                cameraLookAtToggled =
                //RecordTargetToggle.DisplayInMapper =
                LockTargetKey.DisplayInMapper =
                PauseTrackingKey.DisplayInMapper =
                NonCustomModeSmoothSlider.DisplayInMapper =
                AutoLookAtKey.DisplayInMapper =
                value;
                ChangedProperties();
            };
            BlockDataLoadEvent += (XDataHolder BlockData) => { cameraLookAtToggled = CameraLookAtToggle.IsActive; };

            //RecordTargetToggle = AddToggle(LanguageManager.recordTarget, "RecordTarget", recordTarget);
            //RecordTargetToggle.Toggled += (bool value) =>
            //{
            //    recordTarget = value;
            //    ChangedProperties();
            //};
            //BlockDataLoadEvent += (XDataHolder BlockData) => { recordTarget = RecordTargetToggle.IsActive; };

            NonCustomModeSmoothSlider = AddSlider(LanguageManager.firstPersonSmooth, "nonCustomSmooth", firstPersonSmooth, 0, 1, false);
            NonCustomModeSmoothSlider.ValueChanged += (float value) => { firstPersonSmooth = value; ChangedProperties(); };
            BlockDataLoadEvent += (XDataHolder BlockData) => { firstPersonSmooth = NonCustomModeSmoothSlider.Value; };

            LockTargetKey = AddKey(LanguageManager.lockTarget, "LockTarget", lockKeys);

            PauseTrackingKey = AddKey(LanguageManager.pauseTracking, "ResetView", pauseKeys);

            AutoLookAtKey = AddKey(LanguageManager.switchGuideMode, "ActiveSearchKey", activeGuideKeys);

            // Add reference to the camera's buildindex
            fixedCamera = GetComponent<FixedCameraBlock>();
            smoothLook = fixedCamera.CompositeTracker3;
            defaultLocalRotation = smoothLook.localRotation;
            selfIndex = fixedCamera.BuildIndex;


#if DEBUG
            ConsoleController.ShowMessage("摄像机添加进阶属性");
#endif

        }

        public override void DisplayInMapper(bool value)
        {
            if (fixedCamera.CamMode == FixedCameraBlock.Mode.FirstPerson)
            {
                firstPersonMode = true;
            }
            CameraLookAtToggle.DisplayInMapper = value;
            NonCustomModeSmoothSlider.DisplayInMapper = value && cameraLookAtToggled && firstPersonMode;
            AutoLookAtKey.DisplayInMapper = value && cameraLookAtToggled;
            //RecordTargetToggle.DisplayInMapper = value && cameraLookAtToggled;
            LockTargetKey.DisplayInMapper = value && cameraLookAtToggled;
            PauseTrackingKey.DisplayInMapper = value && cameraLookAtToggled;
        }

        //public override void LoadConfiguration(XDataHolder BlockData)
        //{
        //    if (BlockData.HasKey("bmt-" + "CameraTarget"))
        //    {
        //        SaveTargetToDict(BlockData.ReadInt("bmt-" + "CameraTarget"));
        //    }
        //}

        //public override void SaveConfiguration(XDataHolder BlockData)
        //{
        //    if (Machine.Active().GetComponent<TargetScript>().previousTargetDic.ContainsKey(selfIndex))
        //    {
        //        BlockData.Write("bmt-" + "CameraTarget", Machine.Active().GetComponent<TargetScript>().previousTargetDic[selfIndex]);
        //    }
        //}

        protected override void OnBuildingUpdate()
        {
            if (fixedCamera.CamMode != FixedCameraBlock.Mode.FirstPerson && firstPersonMode)
            {
                firstPersonMode = false;
                NonCustomModeSmoothSlider.DisplayInMapper = base.enhancementEnabled && cameraLookAtToggled && firstPersonMode;
            }
            if (fixedCamera.CamMode == FixedCameraBlock.Mode.FirstPerson && !firstPersonMode)
            {
                firstPersonMode = true;
                NonCustomModeSmoothSlider.DisplayInMapper = base.enhancementEnabled && cameraLookAtToggled && firstPersonMode;
            }
        }

        protected override void OnSimulateStart()
        {
            if (cameraLookAtToggled)
            {
                //Initialise the SmoothLook component
                fixedCameraController = FindObjectOfType<FixedCameraController>();

                foreach (var camera in fixedCameraController.cameras)
                {
                    if (camera.BuildIndex == selfIndex)
                    {
                        if (firstPersonMode)
                        {
                            smooth = Mathf.Clamp01(firstPersonSmooth);
                        }
                        else
                        {
                            smooth = Mathf.Clamp01(camera.SmoothSlider.Value);
                        }
                        SetSmoothing();
                    }
                }
                newCamFOV = orgCamFOV = fixedCamera.fovSlider.Value;
                // Initialise
                searchStarted = false;
                pauseTracking = autoSearch = targetAquired = true;
                float searchAngleMax = Mathf.Clamp(Mathf.Atan(Mathf.Tan(fixedCamera.fovSlider.Value * Mathf.Deg2Rad / 2) * Camera.main.aspect) * Mathf.Rad2Deg, 0, 90);
                searchAngle = Mathf.Clamp(searchAngle, 0, searchAngleMax);
                target = null;
                explodedTarget.Clear();
                StopAllCoroutines();

                // If target is recorded, try preset it.
                //if (recordTarget)
                //{
                //    // Trying to read previously saved target
                //    int targetIndex = -1;
                //    BlockBehaviour targetBlock = new BlockBehaviour();
                //    // Read the target's buildIndex from the dictionary
                //    if (!Machine.Active().GetComponent<TargetScript>().previousTargetDic.TryGetValue(selfIndex, out targetIndex))
                //    {
                //        target = null;
                //        return;
                //    }
                //    // Aquire target block's transform from the target's index
                //    try
                //    {

                //        Machine.Active().GetBlockFromIndex(targetIndex, out targetBlock);
                //        target = Machine.Active().GetSimBlock(targetBlock).transform;
                //    }
                //    catch (Exception)
                //    {
                //        ConsoleController.ShowMessage("Cannot get target block's transform");
                //    }
                //}
            }
        }

        protected override void OnSimulateUpdate()
        {
            if (cameraLookAtToggled && fixedCameraController.activeCamera != null)
            {
                if (fixedCameraController.activeCamera.CompositeTracker3 == smoothLook)
                {
                    if (fixedCameraController.activeCamera.CamMode == FixedCameraBlock.Mode.FirstPerson || fixedCameraController.activeCamera.CamMode == FixedCameraBlock.Mode.Custom)
                    {
                        Camera activeCam = FindObjectOfType<MouseOrbit>().cam;
                        if (Input.GetAxis("Mouse ScrollWheel") != 0f)
                        {
                            //FindObjectOfType<MouseOrbit>().cam.fieldOfView = Mathf.Clamp(fixedCameraController.activeCamera.fovSlider.Value + Input.GetAxis("Mouse ScrollWheel"), 1, 90);

                            newCamFOV = Mathf.Clamp(activeCam.fieldOfView - Input.GetAxis("Mouse ScrollWheel") * 50, 1, orgCamFOV);
                        }
                        if (activeCam.fieldOfView != newCamFOV)
                        {
                            activeCam.fieldOfView = Mathf.SmoothStep(activeCam.fieldOfView, newCamFOV, smooth * 0.5f);
                        }
                    }

                    if (AutoLookAtKey.IsReleased)
                    {
                        autoSearch = !autoSearch;
                        DisplayCamMode();
                    }
                    if (PauseTrackingKey.IsReleased)
                    {
                        pauseTracking = !pauseTracking;
                    }
                    if (LockTargetKey.IsReleased)
                    {
                        target = null;
                        if (autoSearch)
                        {
                            targetAquired = searchStarted = false;
                            CameraRadarSearch();
                        }
                        else
                        {
                            if (StatMaster.isMP && StatMaster.isClient)
                            {
                                colliders.Clear();
                                foreach (var player in Playerlist.Players)
                                {
                                    if (!player.isSpectator && player.machine.isSimulating)
                                    {
                                        colliders.AddRange(player.machine.SimulationMachine.GetComponentsInChildren<Collider>(true));
                                    }
                                }
                                foreach (var collider in colliders)
                                {
                                    collider.enabled = true;
                                }
                            }

                            // Aquire the target to look at
                            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                            float manualSearchRadius = 1.25f;
                            RaycastHit[] hits = Physics.SphereCastAll(ray, manualSearchRadius, Mathf.Infinity);
                            Physics.Raycast(ray, out RaycastHit rayHit);
                            for (int i = 0; i < hits.Length; i++)
                            {
                                try
                                {
                                    int index = hits[i].transform.gameObject.GetComponent<BlockBehaviour>().ParentMachine.PlayerID;
                                    target = hits[i].transform;
                                    pauseTracking = false;
                                    //if (recordTarget)
                                    //{
                                    //    SaveTargetToDict(index);
                                    //}
                                    break;
                                }
                                catch { }
                                if (i == hits.Length - 1)
                                {
                                    target = rayHit.transform;
                                    pauseTracking = false;

                                    try
                                    {
                                        int index = rayHit.transform.gameObject.GetComponent<BlockBehaviour>().BuildIndex;
                                        //if (recordTarget)
                                        //{
                                        //    SaveTargetToDict(index);
                                        //}
                                    }
                                    catch { }
                                }
                            }
                            SaveTargetToController();
                            if (StatMaster.isMP && StatMaster.isClient)
                            {
                                foreach (var collider in colliders)
                                {
                                    collider.enabled = false;
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void OnSimulateFixedUpdate()
        {
            if (cameraLookAtToggled && fixedCameraController.activeCamera != null)
            {
                if (fixedCameraController.activeCamera.CompositeTracker3 == smoothLook)
                {
                    if (autoSearch && !targetAquired)
                    {
                        CameraRadarSearch();
                    }
                    if (target != null)
                    {
                        try
                        {
                            if (target.gameObject.GetComponent<TimedRocket>().hasExploded)
                            {
                                //ConsoleController.ShowMessage("Target rocket exploded");
                                timeOfDestruction = Time.time;
                                explodedTarget.Add(target);
                                targetAquired = false;
                                target = null;
                                return;
                            }
                        }
                        catch { }
                        try
                        {
                            if (target.gameObject.GetComponent<ExplodeOnCollideBlock>().hasExploded)
                            {
                                //ConsoleController.ShowMessage("Target bomb exploded");
                                timeOfDestruction = Time.time;
                                explodedTarget.Add(target);
                                targetAquired = false;
                                target = null;
                                return;
                            }
                        }
                        catch { }
                        try
                        {
                            if (target.gameObject.GetComponent<ExplodeOnCollide>().hasExploded)
                            {
                                //ConsoleController.ShowMessage("Target level bomb exploded");
                                timeOfDestruction = Time.time;
                                explodedTarget.Add(target);
                                targetAquired = false;
                                target = null;
                                return;
                            }
                        }
                        catch { }
                        try
                        {
                            if (target.gameObject.GetComponent<ControllableBomb>().hasExploded)
                            {
                                //ConsoleController.ShowMessage("Target grenade exploded");
                                timeOfDestruction = Time.time;
                                explodedTarget.Add(target);
                                targetAquired = false;
                                target = null;
                                return;
                            }
                        }
                        catch { }
                    }

                }
            }
        }

        protected override void OnSimulateLateUpdate()
        {
            if (cameraLookAtToggled && fixedCameraController.activeCamera != null)
            {
                if (fixedCameraController.activeCamera.CompositeTracker3 == smoothLook)
                {
#if DEBUG
                    //ConsoleController.ShowMessage("there are " + explodedTarget.Count + " targets");
#endif
                    if (pauseTracking)
                    {
                        smoothLook.localRotation = Quaternion.Slerp(smoothLook.localRotation, defaultLocalRotation, smoothLerp * Time.deltaTime);
                    }
                    else
                    {
                        if (Time.time - timeOfDestruction >= targetSwitchDelay)
                        {
                            if (target == null)
                            {
                                smoothLook.localRotation = Quaternion.Slerp(smoothLook.localRotation, defaultLocalRotation, smoothLerp * Time.deltaTime);
                            }
                            else
                            {
                                Quaternion quaternion;
                                if (firstPersonMode)
                                {
                                    quaternion = Quaternion.LookRotation(target.position - smoothLook.position, transform.up);
                                }
                                else
                                {
                                    quaternion = Quaternion.LookRotation(target.position - smoothLook.position);
                                }
                                smoothLook.rotation = Quaternion.Slerp(smoothLook.rotation, quaternion, smoothLerp * Time.deltaTime);
                            }
                        }
                    }
                }
            }
        }

        //private void SaveTargetToDict(int BlockID)
        //{
        //    // Make sure the dupicated key exception is handled
        //    try
        //    {
        //        Machine.Active().GetComponent<TargetScript>().previousTargetDic.Add(selfIndex, BlockID);
        //    }
        //    catch (Exception)
        //    {
        //        // Remove the old record, then add the new record
        //        Machine.Active().GetComponent<TargetScript>().previousTargetDic.Remove(selfIndex);
        //        Machine.Active().GetComponent<TargetScript>().previousTargetDic.Add(selfIndex, BlockID);
        //    }
        //}

        private void SetSmoothing()
        {
            float value = 1f - smooth;
            smoothLerp = 16.126f * value * value - 1.286f * value + 0.287f;
        }

        private void CameraRadarSearch()
        {
            if (!searchStarted && autoSearch)
            {
                searchStarted = true;
                StopCoroutine(SearchForTarget());
                StartCoroutine(SearchForTarget());
            }
        }

        private Transform GetMostValuableBlock(HashSet<Machine.SimCluster> simClusterForSearch)
        {
            //Search for any blocks within the search radius for every block in the hitlist
            int[] targetValue = new int[simClusterForSearch.Count];
            Machine.SimCluster[] clusterArray = new Machine.SimCluster[simClusterForSearch.Count];
            List<Machine.SimCluster> maxClusters = new List<Machine.SimCluster>();

            //Start searching
            int i = 0;
            foreach (var simCluster in simClusterForSearch)
            {
                int clusterValue = simCluster.Blocks.Length + 1;
                clusterValue = CalculateClusterValue(simCluster.Base, clusterValue);
                foreach (var block in simCluster.Blocks)
                {
                    clusterValue = CalculateClusterValue(block, clusterValue);
                }
                targetValue[i] = clusterValue;
                clusterArray[i] = simCluster;
                i++;
            }

            //Find the block that has the max number of blocks around it
            int maxValue = targetValue.Max();
            for (i = 0; i < targetValue.Length; i++)
            {
                if (targetValue[i] == maxValue)
                {
                    maxClusters.Add(clusterArray[i]);
                }
            }

            //Find the target that's closest to the centre of the view
            int closestIndex = 0;
            float angleDiffMin = 180f;

            for (i = 0; i < maxClusters.Count; i++)
            {
                float angleDiffCurrent = Vector3.Angle((maxClusters[i].Base.gameObject.transform.position - smoothLook.position).normalized, smoothLook.forward);
                if (angleDiffCurrent < angleDiffMin)
                {
                    closestIndex = i;
                    angleDiffMin = angleDiffCurrent;
                }
            }

            return maxClusters[closestIndex].Base.gameObject.transform;
        }

        IEnumerator SearchForTarget()
        {
            //Grab every machine block at the start of search
            HashSet<Machine.SimCluster> simClusters = new HashSet<Machine.SimCluster>();

            if (StatMaster.isMP)
            {
                foreach (var player in Playerlist.Players)
                {
                    if (!player.isSpectator)
                    {
                        if (player.machine.isSimulating && !player.machine.LocalSim && player.machine.PlayerID != fixedCamera.ParentMachine.PlayerID)
                        {
                            if (fixedCamera.Team == MPTeam.None || fixedCamera.Team != player.team)
                            {
                                simClusters.UnionWith(player.machine.simClusters);
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var cluster in Machine.Active().simClusters)
                {
                    if ((cluster.Base.transform.position - fixedCamera.Position).magnitude > safetyRadius)
                    {
                        simClusters.Add(cluster);
                    }
                }
            }

            //Iternating the list to find the target that satisfy the conditions
            while (!targetAquired && simClusters.Count > 0)
            {
                HashSet<Machine.SimCluster> simClusterForSearch = new HashSet<Machine.SimCluster>(simClusters);
                HashSet<Machine.SimCluster> unwantedClusters = new HashSet<Machine.SimCluster>();

                foreach (var cluster in simClusters)
                {
                    Vector3 positionDiff = cluster.Base.gameObject.transform.position - smoothLook.position;
                    float angleDiff = Vector3.Angle(positionDiff.normalized, smoothLook.forward);
                    bool forward = Vector3.Dot(positionDiff, smoothLook.forward) > 0;
                    bool skipCluster = !(forward && angleDiff < searchAngle) || ShouldSkipCluster(cluster.Base);

                    if (!skipCluster)
                    {
                        foreach (var block in cluster.Blocks)
                        {
                            skipCluster = ShouldSkipCluster(block);
                            if (skipCluster)
                            {
                                break;
                            }
                        }
                    }
                    if (skipCluster)
                    {
                        unwantedClusters.Add(cluster);
                    }
                }

                simClusterForSearch.ExceptWith(unwantedClusters);

                if (simClusterForSearch.Count > 0)
                {
                    target = GetMostValuableBlock(simClusterForSearch);
                    SaveTargetToController();
                    targetAquired = true;
                    pauseTracking = false;
                    searchStarted = false;
                    StopCoroutine(SearchForTarget());
                }
                yield return null;
            }
        }

        private int CalculateClusterValue(BlockBehaviour block, int clusterValue)
        {
            //Some blocks weights more than others
            GameObject targetObj = block.gameObject;
            //A bomb
            if (targetObj.GetComponent<ExplodeOnCollideBlock>())
            {
                if (!targetObj.GetComponent<ExplodeOnCollideBlock>().hasExploded)
                {
                    clusterValue *= 64;
                }
            }
            //A fired and unexploded rocket
            if (targetObj.GetComponent<TimedRocket>())
            {
                if (targetObj.GetComponent<TimedRocket>().hasFired && !targetObj.GetComponent<TimedRocket>().hasExploded)
                {
                    clusterValue *= 128;
                }
            }
            //A watering watercannon
            if (targetObj.GetComponent<WaterCannonController>())
            {
                if (targetObj.GetComponent<WaterCannonController>().isActive)
                {
                    clusterValue *= 16;
                }
            }
            //A flying flying-block
            if (targetObj.GetComponent<FlyingController>())
            {
                if (targetObj.GetComponent<FlyingController>().canFly)
                {
                    clusterValue *= 2;
                }
            }
            //A flaming flamethrower
            if (targetObj.GetComponent<FlamethrowerController>())
            {
                if (targetObj.GetComponent<FlamethrowerController>().isFlaming)
                {
                    clusterValue *= 8;
                }
            }
            //A spinning wheel/cog
            if (targetObj.GetComponent<CogMotorControllerHinge>())
            {
                if (targetObj.GetComponent<CogMotorControllerHinge>().Velocity != 0)
                {
                    clusterValue *= 2;
                }
            }
            return clusterValue;
        }

        private bool ShouldSkipCluster(BlockBehaviour block)
        {
            bool skipCluster = false;
            try
            {
                if (block.gameObject.GetComponent<FireTag>().burning)
                {
                    skipCluster = true;
                }
            }
            catch { }
            try
            {
                if (block.gameObject.GetComponent<TimedRocket>().hasExploded)
                {
                    skipCluster = true;
                }
            }
            catch { }
            try
            {
                if (block.gameObject.GetComponent<ExplodeOnCollideBlock>().hasExploded)
                {
                    skipCluster = true;
                }
            }
            catch { }
            try
            {
                if (block.gameObject.GetComponent<ControllableBomb>().hasExploded)
                {
                    skipCluster = true;
                }
            }
            catch { }

            return skipCluster;
        }

        private void DisplayCamMode()
        {
            if (autoSearch)
            {
                StopCoroutine(DisplayManualMode());
                StartCoroutine(DisplayAutoMode());
            }
            else
            {
                StopCoroutine(DisplayAutoMode());
                StartCoroutine(DisplayManualMode());
            }
        }

        private IEnumerator DisplayAutoMode()
        {
            TextMesh text = new TextMesh
            {
                anchor = TextAnchor.LowerCenter,
                fontSize = 40,
                text = "AUTO AIM MODE"
            };
            return null;
        }

        private IEnumerator DisplayManualMode()
        {
            TextMesh text = new TextMesh
            {
                anchor = TextAnchor.LowerCenter,
                fontSize = 40,
                text = "MANUAL AIM MODE"
            };
            return null;
        }

        private void SaveTargetToController()
        {
            if (target != null)
            {
                FindObjectOfType<Controller>().target = target;
#if DEBUG
                Debug.Log("Target saved to controller");
#endif
            }
        }
    }
}