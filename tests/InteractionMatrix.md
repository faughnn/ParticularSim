# Test Interaction Matrix

Complete inventory of all 490 tests in ParticularLLM, organized by which systems they touch.

## System Interaction Matrix (13×13)

```
                Sand  Water Steam Piston Cluster Belt  Lift  Wall  Furnace Reactions Density Heat  Chunk
Sand             82     21     3      5       4     14    11     5      1       5         12     2     11
Water            21     27     1      -       -      5     4     2      1       2          8     2      3
Steam             3      1    18      -       -      1     2     2      -       2          1     -      1
Piston            5      -     -     57       1      1     1     1      1       -          -     -      -
Cluster           4      -     -      1      69      1     -     1      -       -          1     -      -
Belt             14      5     1      1       1     18     1     2      -       -          1     -      3
Lift             11      4     2      1       -      1    18     1      -       -          -     -      2
Wall              5      2     2      1       1      2     1      5      -       -          1     -      -
Furnace           1      1     -      1       -      -     -     -     27       4          -     1      -
Reactions         5      2     2      -       -      -     -     -      4      18          -    11      -
Density          12      8     1      -       1      1     -     1      -       -         20     -      2
Heat              2      2     -      -       -      -     -     -      1      11          -    14      -
Chunk            11      3     1      -       -      3     2     -      -       -          2     -     13
```

**Legend:**
- Numbers indicate test count for that system pair
- `-` = no meaningful physical interaction (e.g., Piston × Water)
- Diagonal cells are blank (self-tests counted in their row/column)
- Symmetrical matrix (A×B same as B×A)

## Test Files by System Coverage

### Core Infrastructure (59 tests)

#### MaterialTests.cs (23 tests)
**Systems:** Materials definition layer (all systems depend on this)
- CreateDefaults_Returns256Materials
- Air_IsStatic_ZeroDensity
- Sand_IsPowder_Density128
- Water_IsLiquid_Density64_Spread5
- Steam_IsGas_LowDensity
- Oil_IsLiquid_Flammable
- IronOre_IsPowder_MeltsToMoltenIron
- MoltenIron_IsLiquid_FreezesToIron
- Iron_IsStatic_MeltsToMoltenIron
- Coal_IsPowder_Flammable_BurnsToAsh
- Smoke_IsGas
- Sand_DenserThanWater
- Dirt_DenserThanSand
- DensityOrdering_HeavySinksLightRises
- IsBelt_TrueForAllBeltMaterials
- IsBelt_FalseForNonBelt
- IsLift_TrueForLiftMaterials
- IsPiston_TrueForPistonMaterials
- IsStructureMaterial_ClassifiesCorrectly
- IsSoftTerrain_CorrectMaterials
- LiftMaterials_ArePassable
- Ground_IsDiggable
- StructureMaterials_AreStatic_MaxDensity

#### CellWorldTests.cs (13 tests)
**Systems:** Grid infrastructure (all systems depend on this)
- Constructor_InitializesAllCellsToAir
- Constructor_CorrectDimensions
- Constructor_CorrectChunkCounts
- Constructor_NonAlignedWorld_RoundsUpChunks
- SetCell_And_GetCell_RoundTrip
- SetCell_OutOfBounds_DoesNothing
- GetCell_OutOfBounds_ReturnsAir
- SetCell_MarksDirty
- SetCell_ResetsVelocity
- MarkDirtyWithNeighbors_WakesAdjacentChunks
- ResetDirtyState_ClearsDirtyAndSetsActiveLastFrame
- CountActiveCells_CountsNonAir
- Materials_AreInitialized

#### ClusterDataTests.cs (4 tests)
**Systems:** Cluster
- ClusterData_HoldsPixels
- ClusterData_TracksPosition
- ClusterData_TracksVelocity
- ClusterData_PixelsAreReadable

#### WorldUtilsTests.cs (19 tests)
**Systems:** Grid utilities (all systems depend on this)
- CellIndex_CalculatesCorrectly (10 theories)
- CellToChunkX_CorrectDivision (3 theories)
- ChunkToCellX_CorrectMultiplication (3 theories)
- CellToLocalX_CorrectModulo (2 theories)
- IsInBounds_ChecksCorrectly (1 theory)

### Sand (Powder) System (82 tests)

#### PowderTests.cs (8 tests)
**Systems:** Sand
- Sand_MovesDownward_FirstFrame
- Sand_EventuallyFalls_After20Frames
- Sand_FallsToBottomRow
- Sand_StopsAboveStone
- Sand_PilesDiagonally
- Sand_MaterialConservation_LargeAmount
- Sand_DoesNotMoveWhenSupported
- Dirt_PilesSteeper_HigherSlideResistance

#### FractionalGravityTests.cs (13 tests)
**Systems:** Sand, Water (gravity applies to all movable materials)
- Accumulator_NoVelocityOnFirstFrame
- Accumulator_BuildsOverMultipleFrames
- Accumulator_OverflowsAndAccelerates
- Velocity_CapsAtMaxVelocity
- PreVelocity_SandSlidesDiagonally
- PreVelocity_SandFallsStraightIfWalled
- Acceleration_DistanceIncreasesOverTime
- Accumulator_PreservedAcrossFrames
- Collision_ResetsVelocityWhenLanding
- Collision_TransfersToMomentum_WhenFastEnough
- MultiParticle_ConservedDuringFall
- MultiParticle_DifferentMaterialsSameGravity
- Settled_NoFloatingPowderAfterGravity

#### PowderCollisionTests.cs (10 tests)
**Systems:** Sand
- Sand_SpreadsOnImpact_FromHeight
- Sand_ColumnSpreadsIntoTriangle
- Sand_MomentumTransfer_ConservesMaterial
- Sand_DiagonalSlide_MovesOffEdge
- Sand_DiagonalBlocked_TriesOppositeDirection
- Dirt_PilesSteeper_ThanSand
- Sand_ZeroSlideResistance_AlwaysSlides
- Sand_FormsPileOnFlat_ConservesAll
- Sand_PileRoughlySymmetric
- Sand_VelocityZeros_WhenCompletelyBlocked

#### TwoDimensionalMovementTests.cs (10 tests)
**Systems:** Sand, Water (2D physics applies to both)
- Sand_LowRestitution_SettlesQuickly
- Sand_HighVelocityImpact_ScattersWithRestitution
- Dirt_MediumRestitution_ScattersMoreThanSand
- TraceVelocity_DiagonalTrajectory
- TraceVelocity_ShallowAngle
- LiftExit_ArcingTrajectory
- Conservation_ThroughTraceAndCollision
- Conservation_RestitutionDoesNotCreateOrDestroyMaterial
- ZeroRestitution_MaterialStopsOnImpact
- Water_Restitution_SplashOnImpact

### Water (Liquid) System (27 tests)

#### LiquidTests.cs (6 tests)
**Systems:** Water
- Water_FallsDown
- Water_SpreadsHorizontally
- Water_FillsContainer
- Water_MaterialConservation
- Water_LeavesOriginalPosition
- Water_StopsAboveStone

#### LiquidDispersionTests.cs (11 tests)
**Systems:** Water
- Water_FallsDownward
- Water_FallsToFloor_MaterialConserved
- Water_SpreadsHorizontally_OnFloor
- Water_SpreadsBothDirections
- Water_FillsContainer
- Water_SpreadsMoreAfterFalling
- Oil_SpreadsLessThanWater
- Water_NoFloatingLiquid_AfterSettled
- Water_SingleDrop_EventuallySettles
- Water_LargeVolume_MaterialConserved
- Water_DiagonalFall_WhileSliding

### Steam (Gas) System (18 tests)

#### GasTests.cs (5 tests)
**Systems:** Steam
- Steam_RisesUp
- Steam_RisesToTop
- Steam_MaterialConservation
- Steam_LeavesOriginalPosition
- Steam_StopsBelowCeiling

#### GasSpreadTests.cs (10 tests)
**Systems:** Steam
- Steam_RisesUpward
- Steam_RisesToTopOfWorld
- Steam_StopsAtStoneCeiling
- Steam_RisesDiagonallyAroundObstacle
- Steam_SpreadsHorizontallyUnderCeiling
- Steam_MaterialConservation_MultipleParticles
- Steam_ConservedInEnclosure
- Steam_DoesNotDisplaceSand
- Steam_RisesThroughAir_NotThroughStone
- Steam_AcceleratesUpward

### Density System (20 tests)

#### DensityDisplacementTests.cs (7 tests)
**Systems:** Density, Sand, Water
- Sand_SinksThroughWater
- Sand_EndsUpBelowWater
- Water_DoesNotSinkThroughSand
- Stone_BlocksEverything
- DensityDisplacement_MaterialConservation_MixedScene

#### DensityEdgeCaseTests.cs (11 tests)
**Systems:** Density, Sand, Water, Steam
- EqualDensity_SandDoesNotDisplaceSand
- EqualDensity_WaterDoesNotDisplaceWater
- ThreeMaterials_SandSinks_WaterMiddle_OilFloats
- OilFloatsOnWater_AtRest
- DensityLayering_SandOverWater_InContainer
- MassDisplacement_ManySandThroughWater
- StaticMaterial_BlocksAllMovement
- WallMaterial_BlocksAllMovement
- PassableLiftMaterial_AllowsMovement
- Dirt_SinksThroughWater_HigherDensity
- Dirt_DoesNotDisplaceSand_SimilarDensity
- Conservation_FourMaterials_AllConserved

### Heat Transfer & Reactions (25 tests)

#### HeatTransferTests.cs (14 tests)
**Systems:** Heat
- HotConductor_CoolsTowardAmbient
- HotConductor_EventuallyReachesAmbient
- ColdConductor_WarmsTowardAmbient
- AdjacentConductors_TemperaturesDiffuse
- AdjacentConductors_Equilibrate_OverTime
- HeatSpreads_AllFourDirections
- NonConductor_KeepsTemperature
- NonConductor_BlocksHeatSpread
- DoubleBuffered_SymmetricDiffusion
- Temperature_ClampedToByteRange
- Water_ConductsHeat
- ConductionRate_25PercentBlend
- HeatDisabled_NoTemperatureChange

#### MaterialReactionTests.cs (18 tests)
**Systems:** Reactions, Heat, Sand, Water, Steam
- IronOre_MeltsAtThreshold
- IronOre_DoesNotMelt_BelowThreshold
- Iron_MeltsToMoltenIron
- Melting_ResetsVelocity
- MoltenIron_FreezesToIron
- MoltenIron_DoesNotFreeze_AboveThreshold
- Steam_FreezesToWater
- Water_BoilsToSteam
- Water_DoesNotBoil_BelowThreshold
- Boiling_SetsUpwardVelocity
- Coal_IgnitesAtThreshold
- Coal_DoesNotIgnite_BelowThreshold
- BurningCoal_EmitsHeat
- BurningCoal_EventuallyBecomesAsh
- Oil_IgnitesAtLowerTemp_ThanCoal
- PhaseChange_ConservesCellCount
- ReactionsDisabled_NoPhaseChanges
- Water_Boil_Then_Freeze_RoundTrip

### Processing Order & Chunk System (38 tests)

#### ProcessingOrderTests.cs (5 tests)
**Systems:** Chunk, Sand, Water, Lift
- BottomToTop_SandColumnFallsInOneFrame
- AlternatingX_NoDirectionalBias
- LiftUpward_WorksWithBottomToTopProcessing
- ExtendedRegion_SandFallsAcrossChunkBoundary
- WaterSpread_NotBiasedLeftOrRight

#### ProcessingOrderEdgeCases.cs (7 tests)
**Systems:** Chunk, Sand, Water
- FrameUpdated_PreventsDoubleProcessing
- FlatMode_Deterministic
- FourPassMode_Deterministic
- BothModes_ConserveMaterial
- BottomToTop_FallingCascades
- Conservation_MixedMaterials_BothModes

#### CheckerboardGroupTests.cs (9 tests)
**Systems:** Chunk
- GroupAssignment_CorrectCheckerboardPattern
- SameGroupChunks_AreAtLeastTwoApart
- AllActiveChunks_AreAssignedToExactlyOneGroup
- FourPassAndFlat_BothConserveMaterials_ButMayDiffer
- FourPassMode_MaterialConservation_MultiChunk
- FourPassMode_Deterministic
- FourPassMode_CrossChunkBoundary_SandFalls
- InactiveChunks_NotAssignedToAnyGroup

#### ChunkBoundaryTests.cs (9 tests)
**Systems:** Chunk, Sand, Water, Belt
- Sand_FallsAcrossVerticalChunkBoundary
- Sand_FallsAcrossHorizontalChunkBoundary
- ChunkWakesNeighbor_WhenCellVacatesBoundary
- Conservation_MaterialsAcrossAllChunks
- Conservation_WaterAcrossChunkBoundaries
- ChunkWithBelt_StaysActive
- FourPassMode_ConservesAcrossChunks
- FourPassMode_SandSettlesToFloor

#### DeterminismTests.cs (3 tests)
**Systems:** All simulation systems (integration test)
- SameSetup_ProducesIdenticalState
- ComplexScenario_Deterministic
- Determinism_MultipleRuns_AllIdentical

### Belt System (18 tests)

#### BeltPlacementTests.cs (9 tests)
**Systems:** Belt
- PlaceBelt_InAir_Succeeds
- PlaceBelt_SnapsToGrid
- PlaceBelt_OnStone_Fails
- PlaceBelt_OnSoftTerrain_CreatesGhost
- PlaceBelt_WritesBeltMaterialToCells
- PlaceBelt_Overlap_Fails
- RemoveBelt_ClearsToAir
- AdjacentBelts_SameDirection_Merge
- AdjacentBelts_DifferentDirection_DontMerge

#### BeltSimulationTests.cs (6 tests)
**Systems:** Belt, Sand, Water
- Belt_MovesSandRight
- Belt_MovesSandLeft
- Belt_MovesWater
- Belt_MaterialConservation
- Belt_DoesNotMoveSandBelowSurface
- Belt_SpeedIsBased_OnFrameCount

#### BeltSpeedStackTests.cs (11 tests)
**Systems:** Belt, Sand, Water, Steam, Chunk
- Belt_Speed3_Moves1CellPer3Frames
- Belt_MovesEntireStack
- Belt_StackBlockedByObstacle
- Belt_LeftDirection_MovesSandLeft
- Belt_MovesWaterButNotSteam
- Belt_MergedBelt_TransportsAcrossBlocks
- Belt_SandFallsOffEndOfBelt
- Belt_ManySand_AllConserved

### Lift System (18 tests)

#### LiftPlacementTests.cs (6 tests)
**Systems:** Lift
- PlaceLift_InAir_Succeeds
- PlaceLift_WritesPassableMaterialToCells
- AdjacentLifts_Vertically_Merge
- AdjacentLifts_Horizontally_DontMerge
- RemoveLift_ClearsToAir
- RemoveLift_MiddleOfMerged_SplitsInTwo

#### LiftSimulationTests.cs (5 tests)
**Systems:** Lift, Sand, Water
- Lift_PushesSandUpward
- Lift_PushesWaterUpward
- Lift_MaterialConservation
- Lift_IsPassable_SandEntersLiftZone
- Lift_RestoresLiftMaterial_WhenCellMovesOut

#### LiftExitFountainTests.cs (11 tests)
**Systems:** Lift, Sand, Water, Chunk
- Lift_NetUpwardForce_FirstFrame
- Lift_SandRisesToTopAndExits
- Lift_ExitRow_SetsLateralVelocity
- Lift_FountainEffect_MaterialSpreadsHorizontally
- Lift_NoOscillationLoop
- Lift_FountainSymmetry_LeftAndRight
- Lift_VelocityX_ConsumedDuringRise
- Lift_MultipleSand_AllExit
- Lift_WaterExitsAndSpreads
- Lift_TilesRestoredAfterMaterialPasses
- Lift_ConservationStress_ManySandThroughLift

### Wall System (5 tests)

#### WallPlacementTests.cs (5 tests)
**Systems:** Wall
- PlaceWall_InAir_Succeeds
- PlaceWall_WritesWallMaterial
- PlaceWall_OnStone_Fails
- RemoveWall_ClearsToAir

### Ghost Activation System (17 tests)

#### GhostActivationTests.cs (9 tests)
**Systems:** Belt, Lift, Wall
- GhostBelt_ActivatesWhenTerrainCleared
- GhostBelt_DoesNotActivate_WithRemainingTerrain
- GhostBelt_DoesNotActivate_WithSandRemaining
- GhostWall_ActivatesWhenTerrainCleared
- GhostWall_DoesNotActivate_WithRemainingTerrain
- GhostLift_ActivatesWhenGroundCleared_AllowsPowder
- GhostLift_DoesNotActivate_WithGroundRemaining
- GhostLift_ActivatesWithWaterPresent
- GhostLift_WritesLiftMaterial_OnlyToAirCells

#### GhostBlockingTests.cs (8 tests)
**Systems:** Belt, Wall, Sand, Water
- GhostWall_BlocksSandFromEntering
- GhostWall_BlocksSandFromEntering_AirArea
- GhostWall_MaterialInsideCanMoveOut
- GhostBelt_BlocksSandFromEntering
- GhostBelt_MaterialInsideCanMoveOut
- GhostWall_MaterialConservation_MultipleSandGrains
- GhostWall_WaterInsideCanBeDisplacedBySand
- GhostWall_SandSettlesOnTopOfGhostArea

### Furnace System (27 tests)

#### FurnaceTests.cs (27 tests)
**Systems:** Furnace, Heat, Reactions, Sand, Water
- PlaceFurnace_CreatesWallsAndHollowInterior
- PlaceFurnace_MinimumSize3x3
- PlaceFurnace_RejectsTooSmall
- PlaceFurnace_RejectsOutOfBounds
- PlaceFurnace_RejectsNonAirPerimeter
- PlaceFurnace_AllowsNonAirInterior
- RemoveFurnace_ClearsWalls
- RemoveFurnace_PreservesInteriorMaterials
- RemoveFurnace_InvalidId_ReturnsFalse
- Heating_IncreasesInteriorTemperature
- Heating_CappedAtMaxTemp
- Heating_SkipsAirCells
- Heating_AccumulatesOverFrames
- Off_DoesNotHeatInterior
- SetState_SwitchesHeatingToOff
- Furnace_MeltsIronOre
- Furnace_BoilsWater
- Furnace_IgnitesCoal
- Furnace_MaxTemp_PreventsPhaseChange
- FurnaceWalls_BlockMaterial
- FurnaceWalls_AreStatic
- FurnaceWalls_ConductHeat
- Furnace_MeltingConservesMaterials
- MultipleFurnaces_IndependentHeating
- RemoveOneFurnace_OtherContinues
- Heating_OnlyAffectsInterior
- Furnace_SmeltingScenario_IronOreToMoltenToIron

### Piston System (57 tests)

#### PistonTests.cs (57 tests)
**Systems:** Piston, Cluster, Sand
- SnapToGrid_SnapsCorrectly (8 theories)
- SnapToGrid_NegativeValues (5 theories)
- MotorCycle_StartsRetracted
- MotorCycle_DwellPhases
- MotorCycle_ReachesFullExtension
- MotorCycle_CompletesFullCycle
- PlacePiston_Right_CreatesBaseBar
- PlacePiston_Left_BaseOnRight
- PlacePiston_Down_BaseOnTop
- PlacePiston_Up_BaseOnBottom
- PlacePiston_OutOfBounds_ReturnsFalse
- PlacePiston_OverlapExisting_ReturnsFalse
- PlacePiston_BlockedByStone_ReturnsFalse
- PlacePiston_ClearsSoftTerrain
- RemovePiston_ClearsAndUnregisters
- RemovePiston_NoPiston_ReturnsFalse
- Extend_PushesSandRight
- Extend_StalledByStone
- Extend_WriteFillBehindPlate
- Retract_ClearsFillArea
- PlacePiston_WithClusterManager_CreatesPlateCluster
- PlacePiston_WithoutClusterManager_NoCluster
- PlateCluster_MovesWithStrokeT
- PlacePiston_AllDirections_Succeed (4 theories)
- Pipeline_PistonPushesSand
- Pipeline_MultiplePistons_IndependentPlacement
- HasPistonAt_InsidePiston_ReturnsTrue
- HasPistonAt_OutsidePiston_ReturnsFalse
- Constants_MaxTravel_Is12
- Constants_CycleFrames_Is180

### Cluster System (69 tests)

#### ClusterDataLookupTests.cs (17 tests)
**Systems:** Cluster
- BuildPixelLookup_SinglePixel
- BuildPixelLookup_Square3x3
- BuildPixelLookup_LShape_HasGaps
- BuildPixelLookup_MixedMaterials
- BuildPixelLookup_EmptyCluster
- AddPixel_InvalidatesLookup
- LocalBounds_Correct
- ForEachWorldCell_NoRotation_MapsDirectly
- ForEachWorldCell_Rotation90_RotatesCorrectly
- ForEachWorldCell_OutOfBounds_Skipped
- ForEachWorldCell_PixelCount_MatchesAtNoRotation
- ForEachWorldCell_EarlyExit_Works
- ShouldSkipSync_Sleeping_SamePosition_ReturnsTrue
- ShouldSkipSync_NotSleeping_ReturnsFalse
- ShouldSkipSync_MachinePart_ReturnsFalse
- ShouldSkipSync_Moved_ReturnsFalse
- Wake_ClearsSleepState

#### ClusterFactoryTests.cs (12 tests)
**Systems:** Cluster
- CreateCluster_SetsPositionAndRegisters
- CreateCluster_CalculatesMass
- CreateCluster_MomentOfInertia_Nonzero
- CreateCluster_MomentOfInertia_LargerForWiderShapes
- CreateSquareCluster_CorrectPixelCount
- CreateCircleCluster_ReasonablePixelCount
- CreateLShapeCluster_HasPixels
- CreateClusterFromRegion_ExtractsCells
- CreateClusterFromRegion_MaterialConservation
- CreateClusterFromRegion_EmptyRegion_ReturnsNull
- CreateClusterFromRegion_ClearsOwnedCells
- CreateClusterFromRegion_Position_IsRegionCenter

#### ClusterPhysicsTests.cs (19 tests)
**Systems:** Cluster
- Gravity_IncreasesVelocityY
- Gravity_AccumulatesOverFrames
- FreeFall_MovesDownward
- Collision_LandsOnStoneFloor
- Collision_StopsVerticalVelocity
- Collision_WallBlocksHorizontal
- Collision_Restitution_ReducesBounceVelocity
- Collision_Friction_ReducesHorizontalVelocity
- Sleep_AfterLanding
- Sleep_Skips_PhysicsStep
- Sleep_MachinePart_NeverSleeps
- Sleep_ActiveForce_PreventsSleep
- WorldBoundary_BottomEdge_IsCollision
- WorldBoundary_LeftEdge_IsCollision
- OverlapsStatic_DetectsStone
- OverlapsStatic_IgnoresMovableMaterials
- OverlapsStatic_DetectsWallMaterial
- Rotation_Updates
- Rotation_CollisionReverts

#### ClusterManagerTests.cs (11 tests)
**Systems:** Cluster, Sand, Density
- AllocateId_StartsAt1
- AllocateId_ReusesReleasedIds
- Register_TracksCluster
- Unregister_RemovesCluster
- StepAndSync_ClusterFalls
- StepAndSync_PixelsAppearInGrid
- StepAndSync_PixelOwnership
- StepAndSync_ClearPreviousPosition
- Displacement_PushesMovableMaterial
- Displacement_VelocityTransfer_LightMaterialPushedHarder
- Displacement_MaterialConservation_MultiplePixels
- Displacement_CongestedArea_MaterialConserved
- Displacement_ColumnFullAboveAndBelow_FindsAirElsewhere
- StepAndSync_LandsOnFloor_Settles
- RemoveCluster_ClearsPixelsAndUnregisters
- MultipleClusters_Independent
- SleepingCluster_SkipsSync
- Pipeline_IntegratedWithCellSimulator

#### ClusterFractureTests.cs (10 tests)
**Systems:** Cluster
- FractureCluster_SplitsIntoMultiplePieces
- FractureCluster_MaterialConservation
- FractureCluster_MixedMaterials_AllPreserved
- FractureCluster_TooSmall_NoFracture
- FractureCluster_InheritsVelocity
- FractureCluster_Deterministic_SameSeed
- FractureCluster_DifferentSeeds_DifferentResults
- FractureCluster_AllSubClusters_MinimumSize
- CheckAndFracture_SkipsSleeping
- CheckAndFracture_SkipsMachineParts
- CheckAndFracture_BelowThreshold_NoFracture
- CheckAndFracture_AboveThreshold_Fractures
- FractureCluster_CircleShape_MaterialConservation

#### ClusterCollisionTests.cs (24 tests)
**Systems:** Cluster
- FindOverlapping_NoOverlap_ReturnsNull
- FindOverlapping_ExactOverlap_ReturnsOther
- FindOverlapping_PartialOverlap_ReturnsOther
- FindOverlapping_JustTouching_NoOverlap
- FindOverlapping_SkipsSelf
- FindOverlapping_EmptyCluster_ReturnsNull
- ResolveCollision1D_EqualMass_SwapsVelocities
- ResolveCollision1D_ConservesMomentum
- ResolveCollision1D_HeavyPushesLight
- ResolveCollision1D_InelasticCollision_ReducesRelativeSpeed
- ResolveCollision1D_YAxis_Works
- ResolveCollision1D_WakesSleepingCluster
- StepCluster_FallingCluster_LandsOnAnother
- StepCluster_HorizontalCollision_Bounce
- StepCluster_ClusterCollision_SetsOnGround
- StepCluster_RotationBlockedByCluster
- Pipeline_TwoClusters_FallAndStack
- Pipeline_ThreeClusters_Stack
- Pipeline_HorizontalApproach_MaterialConservation
- Pipeline_CollisionWakesSleepingCluster
- GetWorldAABB_NoRotation_MatchesPixelBounds
- GetWorldAABB_Rotated_EnlargesBox
- GetWorldAABB_EmptyCluster_PointAtCenter

### Global Invariants (10 tests)

#### GlobalInvariantSweep.cs (10 tests)
**Systems:** All systems (validation layer)
- Invariants_SandPile
- Invariants_WaterPool
- Invariants_MixedMaterials
- Invariants_DensityLayering_Settled
- Invariants_SteamEnclosure
- Invariants_SandOnBelt
- Invariants_SandThroughLift
- Invariants_LargeScale_AllMaterials
- Invariants_FourPassMode_Conservation
- Invariants_SettledSand_NoFloating

### Cross-System Integration Tests (29 tests)

#### CrossSystemIntegrationTests.cs (13 tests)
**Systems:** Piston, Belt, Lift, Wall, Furnace, Sand, Water, Density
- Sand_FallsOntoBelt_GetsTransported
- Sand_ThroughLift_LandsOnFloor
- Lift_Exit_Sand_LandsOnBelt
- Wall_BlocksBeltTransport
- Lift_SandAndWater_BothExit
- FullPipeline_BeltLiftWall_Conservation
- Lift_SandAndWater_DensityOrder_AfterExit

#### ScenarioTests.cs (3 tests)
**Systems:** Sand, Water, Chunk
- LargeWorld_SandAndWater_MaterialConservation
- MultiChunk_SandCrossesChunkBoundaries
- GravityConsistency_AllPowdersFallAtSameRate

#### BeltLiftComboTests.cs (3 tests)
**Systems:** Belt, Lift, Wall, Sand
- Sand_OnBelt_FallsOffEnd
- WallBlocksSandFalling
- FullPipeline_MaterialConservation

#### InteractionMatrixTests.cs (19 tests)
**Systems:** Piston, Cluster, Belt, Lift, Wall, Furnace, Sand, Water, Steam, Reactions, Density
- Piston_PushesSand_SandFalls
- Piston_PushesSand_OntoBelt
- Piston_PushesSand_IntoLift
- Piston_PushesSand_BlockedByWall
- Piston_PushesSand_IntoFurnace
- Cluster_LandsOnWall_StopsAbove
- Cluster_OverBelt_BeltCannotMoveCluster
- Cluster_DisplacesSand_SandFallsToFloor
- Water_OnBelt_Transported
- Water_InLift_PushedUp
- Water_BlockedByWall
- Gas_InLift_AcceleratedUpward
- Gas_BlockedByWall
- Gas_OnBelt_Unaffected
- Melting_ResetsVelocity_MaterialRefalls
- Boiling_GivesUpwardVelocity
- Lift_PushesUp_WallBlocksAbove
- DensitySorting_OnBelt_PreservedDuringTransport

---

## Summary Statistics

- **Total test files:** 41
- **Total test methods:** 490
- **Core infrastructure tests:** 59
- **Material behavior tests:** 147 (Sand: 82, Water: 27, Steam: 18, Density: 20)
- **Heat & reactions tests:** 25
- **Structure tests:** 114 (Belt: 18, Lift: 18, Wall: 5, Furnace: 27, Piston: 57, Ghost: 17)
- **Cluster tests:** 69
- **Chunk system tests:** 38
- **Integration tests:** 29
- **Global invariant tests:** 10

## Coverage Gaps (Interactions with 0 tests)

Based on the matrix, these system pairs have no direct interaction tests:
- Piston × Water (no meaningful interaction — pistons push cells, water flows)
- Piston × Steam (no meaningful interaction)
- Cluster × Steam (clusters are solid, steam is gas — no special interaction)
- Furnace × Steam (steam doesn't react in furnaces, only boils/freezes)
- Heat × Steam (covered through Reactions, not separate)
- Cluster × Lift (clusters don't enter lifts)
- Several others marked with `-` in the matrix

These are intentional gaps where the systems don't meaningfully interact in the physics simulation.
