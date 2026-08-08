'(C) Copyright 2008 by Autodesk, Inc.
'
'
'By using this code, you are agreeing to the terms
'and conditions of the License Agreement that appeared
'and was accepted upon download or installation
'(or in connection with the download or installation)
'of the Autodesk software in which this code is included.
'All permissions on use of this code are as set forth
'in such License Agreement provided that the above copyright
'notice appears in all authorized copies and that both that
'copyright notice and the limited warranty and
'restricted rights notice below appear in all supporting
'documentation.
'
'AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
'AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
'MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
'DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
'UNINTERRUPTED OR ERROR FREE.
'
'Use, duplication, or disclosure by the U.S. Government is subject to
'restrictions set forth in FAR 52.227-19 (Commercial Computer
'Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
'(Rights in Technical Data and Computer Software), as applicable.

Option Explicit On
Option Strict Off



Imports System.Math

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.DatabaseServices
Imports Autodesk.Civil.Runtime
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil

Public Class CrownedLane
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: CrownedLane
    '
    '    Description:    This subassembly creates a crowned lane that has separate 
    '    subbase slope control, as well as the ability to control the subbase crown 
    '    location via by specifying a targeted alignment.



    'Logical Names: Name                    Type            Optional    Default value       Description
    '                -----------------------------------------------------------------------------------
    '               Left Width                   Alignment       Yes         none                May be used to override the fixed left lane Width and tie the edge-of-lane to an offset alignment
    '               Left Outside Elevation       Profile         Yes         none                May be used to override the normal lane slope and tie the outer edge of the left travel lane to the elevation of a profile
    '               Right Width                  Alignment       Yes         none                May be used to override the fixed right lane Width and tie the edge-of-lane to an offset alignment
    '               Right Outside Elevation      Profile         Yes         none                May be used to override the normal lane slope and tie the outer edge of the right travel lane to the elevation of a profile
    '               Base Centerline              Alignment       Yes         none                May be used to override the crown location  of the base and subbase materials

    '   Parameters: Name            Type    Optional    Default Value   Description
    '                ---------------------------------------------------------------
    '               Left Lane Width           Double  no          12.0
    '               Right Lane Width          Double  no          12.0
    '               DefaultSlope    Double  no          -0.02           default inside slope value if superelevation not defined
    '               BaseSlope       Double  no          -0.03           Specifies the slope of the base and subbase materials.  In superelevation will be calculated as the difference between Default slope and base slope, then added to Superelevation value. 
    '               Pave1Depth      Double  no          0.083           zero to omit this layer
    '               Pave2Depth      Double  no          0.083           zero to omit this layer
    '               BaseDepth       Double  no          0.33
    '               SubbaseDepth    Double  no          1.0
    '              
    '   Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '               Lane %Slope               Double            % slope of the lane 
    Private Const LeftLaneWidthDefault = 12.0#
    Private Const RightLaneWidthDefault = 12.0#
    Private Const DefaultLeftSlopeDefault = -0.02
    Private Const DefaultRightSlopeDefault = -0.02
    Private Const BaseLeftSlopeDefault = -0.03
    Private Const BaseRightSlopeDefault = -0.03
    Private Const Pave1DepthDefault = 0.083
    Private Const Pave2DepthDefault = 0.083
    Private Const BaseDepthDefault = 0.333
    Private Const SubbaseDepthDefault = 1.0#


    ' --------------------------------------------------------------------------
    ' Returns logical names used by this script
    Protected Overrides Sub GetLogicalNamesImplement(ByVal corridorState As CorridorState)

        MyBase.GetLogicalNamesImplement(corridorState)
        ''*********************************************************
        'On Error GoTo ErrorHandler

        ' Retrieve parameter buckets from the corridor state
        Dim oParamslong As ParamLongCollection
        oParamslong = corridorState.ParamsLong

        ' Add the logical names we use in this script
        Dim oParamLong As ParamLong

        oParamLong = oParamslong.Add("Left Width Alignment", ParamLogicalNameType.OffsetTarget)
        oParamLong.DisplayName = "11090"

        oParamLong = oParamslong.Add("Left Outside Elevation", ParamLogicalNameType.ElevationTarget)
        oParamLong.DisplayName = "11091"

        oParamLong = oParamslong.Add("Right Width Alignment", ParamLogicalNameType.OffsetTarget)
        oParamLong.DisplayName = "11092"


        oParamLong = oParamslong.Add("Right Outside Elevation", ParamLogicalNameType.ElevationTarget)
        oParamLong.DisplayName = "11093"

        oParamLong = oParamslong.Add("Base Centerline", ParamLogicalNameType.OffsetTarget)
        oParamLong.DisplayName = "11094"


    End Sub

    ' --------------------------------------------------------------------------
    ' Returns input parameters required by this script
    Protected Overrides Sub GetInputParametersImplement(ByVal corridorState As CorridorState)

        MyBase.GetInputParametersImplement(corridorState)
        ''*********************************************************
        'On Error GoTo ErrorHandler ''

        Dim oParamsDouble As ParamDoubleCollection
        oParamsDouble = corridorState.ParamsDouble

        Dim oParamslong As ParamLongCollection
        oParamslong = corridorState.ParamsLong
        ' Add the input parameters we use in this script

        Dim oParam As IParam

        oParam = oParamsDouble.Add("LeftLaneWidth", LeftLaneWidthDefault)
        oParam.DisplayName = "11005"
        oParam.Description = "11006"
        oParam = oParamsDouble.Add("RightLaneWidth", RightLaneWidthDefault)
        oParam.DisplayName = "11007"
        oParam.Description = "11008"
        oParam = oParamsDouble.Add("DefaultLeftSlope", DefaultLeftSlopeDefault)
        oParam.DisplayName = "11009"
        oParam.Description = "11010"
        oParam = oParamsDouble.Add("DefaultRightSlope", DefaultRightSlopeDefault)
        oParam.DisplayName = "11025"
        oParam.Description = "11026"
        oParam = oParamsDouble.Add("BaseLeftSlope", BaseLeftSlopeDefault)
        oParam.DisplayName = "11011"
        oParam.Description = "11012"
        oParam = oParamsDouble.Add("BaseRightSlope", BaseRightSlopeDefault)
        oParam.DisplayName = "11027"
        oParam.Description = "11028"
        oParam = oParamsDouble.Add("Pave1Depth", Pave1DepthDefault)
        oParam.DisplayName = "11013"
        oParam.Description = "11014"
        oParam = oParamsDouble.Add("Pave2Depth", Pave2DepthDefault)
        oParam.DisplayName = "11015"
        oParam.Description = "11016"
        oParam = oParamsDouble.Add("BaseDepth", BaseDepthDefault)
        oParam.DisplayName = "11017"
        oParam.Description = "11018"
        oParam = oParamsDouble.Add("SubBaseDepth", SubbaseDepthDefault)
        oParam.DisplayName = "11019"
        oParam.Description = "11020"

    End Sub

    ' --------------------------------------------------------------------------
    ' Returns output parameters returned by this script
    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)

        MyBase.GetOutputParametersImplement(corridorState)
        ' '*********************************************************
        'On Error GoTo ErrorHandler


        ' Retrieve parameter buckets from the corridor state
        Dim oParamsDouble As ParamDoubleCollection
        oParamsDouble = corridorState.ParamsDouble

        ' Add the output parameters we use in this script
        Dim oParam As IParam

        oParam = oParamsDouble.Add("Left Lane Slope", DefaultLeftSlopeDefault)
        oParam.DisplayName = "11080"
        If Not oParam Is Nothing Then oParam.Access = ParamAccessType.Output

        oParam = oParamsDouble.Add("Right Lane Slope", DefaultRightSlopeDefault)
        oParam.DisplayName = "11081"
        If Not oParam Is Nothing Then oParam.Access = ParamAccessType.Output

        oParam = oParamsDouble.Add("LeftLaneWidth", LeftLaneWidthDefault)
        oParam.DisplayName = "11005"
        If Not oParam Is Nothing Then oParam.Access = ParamAccessType.InputAndOutput

        oParam = oParamsDouble.Add("RightLaneWidth", RightLaneWidthDefault)
        oParam.DisplayName = "11007"
        If Not oParam Is Nothing Then oParam.Access = ParamAccessType.InputAndOutput

    End Sub

    ' --------------------------------------------------------------------------
    ' Computes the SideSlope
    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager = HostApplicationServices.WorkingDatabase.TransactionManager

        '*********************************************************
        'On Error GoTo ErrorHandler

        '---------------------------------------------------------

        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget

        Dim oParamslong As ParamLongCollection
        oParamslong = corridorState.ParamsLong
        ' Retrieve parameter buckets from the corridor state

        Dim oParamsDouble As ParamDoubleCollection
        oParamsDouble = corridorState.ParamsDouble

        '---------------------------------------------------------
        ' now fetch a few parameters we're interested in

        '*********************************************************
        'On Error Resume Next

        Dim vLeftLaneWidth As Double
        Try
            vLeftLaneWidth = oParamsDouble.Value("LeftLaneWidth")
        Catch
            vLeftLaneWidth = LeftLaneWidthDefault
        End Try

        Dim vRightLaneWidth As Double
        Try
            vRightLaneWidth = oParamsDouble.Value("RightLaneWidth")
        Catch
            vRightLaneWidth = RightLaneWidthDefault
        End Try

        Dim vDefaultLeftSlope As Double
        Try
            vDefaultLeftSlope = oParamsDouble.Value("DefaultLeftSlope")
        Catch
            vDefaultLeftSlope = DefaultLeftSlopeDefault
        End Try

        Dim vDefaultRightSlope As Double
        Try
            vDefaultRightSlope = oParamsDouble.Value("DefaultRightSlope")
        Catch
            vDefaultRightSlope = DefaultRightSlopeDefault
        End Try

        Dim vBaseLeftSlope As Double
        Try
            vBaseLeftSlope = oParamsDouble.Value("BaseLeftSlope")
        Catch
            vBaseLeftSlope = BaseLeftSlopeDefault
        End Try

        Dim vBaseRightSlope As Double
        Try
            vBaseRightSlope = oParamsDouble.Value("BaseRightSlope")
        Catch
            vBaseRightSlope = BaseRightSlopeDefault
        End Try

        Dim vPave1Depth As Double
        Try
            vPave1Depth = oParamsDouble.Value("Pave1Depth")
        Catch
            vPave1Depth = Pave1DepthDefault
        End Try

        Dim vPave2Depth As Double
        Try
            vPave2Depth = oParamsDouble.Value("Pave2Depth")
        Catch
            vPave2Depth = Pave2DepthDefault
        End Try

        Dim vBaseDepth As Double
        Try
            vBaseDepth = oParamsDouble.Value("BaseDepth")
        Catch
            vBaseDepth = BaseDepthDefault
        End Try

        Dim vSubbaseDepth As Double
        Try
            vSubbaseDepth = oParamsDouble.Value("SubBaseDepth")
        Catch
            vSubbaseDepth = SubbaseDepthDefault
        End Try

        '------------------------------------------------------------------------
        'Check user input

        If vLeftLaneWidth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "vLeftLaneWidth", "CrownedLane")
            vLeftLaneWidth = LeftLaneWidthDefault
        End If

        If vRightLaneWidth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "vRightLaneWidth", "CrownedLane")
            vRightLaneWidth = RightLaneWidthDefault
        End If

        If vPave1Depth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "Pave1Depth", "CrownedLane")
            vPave1Depth = Pave1DepthDefault
        End If

        If vPave2Depth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "Pave2Depth", "CrownedLane")
            vPave2Depth = Pave2DepthDefault
        End If

        If vBaseDepth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "BaseDepth", "CrownedLane")
            vBaseDepth = BaseDepthDefault
        End If

        If vSubbaseDepth < 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanZero, "SubBaseDepth", "CrownedLane")
            vSubbaseDepth = SubbaseDepthDefault
        End If

        'user input check finished
        '------------------------------------------------------------------------
        '---------------------------------------------------------
        ' Get alignment we're currently working from
        Dim oAlignment As Alignment = Nothing
        Dim oAlignmentId As ObjectId

        '---------------------------------------------------------
        ' Set up projection ray origin
        Dim oOrigin As New PointInMem
        ' get current alignment and adjusted origin point
        Utilities.GetAlignmentAndOrigin(corridorState, oAlignmentId, oOrigin)

        'Dim oParam As IParam

        Dim offsetLeftTarget As WidthOffsetTarget 'width or offset target
        offsetLeftTarget = Nothing
        Dim offsetRightTarget As WidthOffsetTarget 'width or offset target
        offsetRightTarget = Nothing
        Dim baseCenterline As WidthOffsetTarget 'width or offset target
        baseCenterline = Nothing
        Dim dXOnTarget As Double
        Dim dYOnTarget As Double
        Dim dCrownOffset As Double = 0
        Dim dLeftSlope As Double
        dLeftSlope = vDefaultLeftSlope
        Dim dRightSlope As Double
        dRightSlope = vDefaultRightSlope
        If (corridorState.Mode <> CorridorMode.Layout) Then
            '---------------------------------------------------------
            ' try to compute the distance from the current station on the current alignment
            oAlignment = tm.GetObject(oAlignmentId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, False)
            Try
                baseCenterline = oParamsOffsetTarget.Value("Base Centerline")
                Dim dTempOffset As Double
                If True = Utilities.CalcAlignmentOffsetToThisAlignment(oAlignmentId, _
                                                             oOrigin.Station, _
                                                             baseCenterline, _
                                                             dTempOffset, _
                                                             dXOnTarget, dYOnTarget) Then
                    dCrownOffset = dTempOffset
                End If
            Catch
            End Try

            Try
                offsetLeftTarget = oParamsOffsetTarget.Value("Left Width Alignment")
                Dim dTempOffset As Double
                If True = Utilities.CalcAlignmentOffsetToThisAlignment(oAlignmentId, _
                                                             oOrigin.Station, _
                                                             offsetLeftTarget, _
                                                             dTempOffset, _
                                                             dXOnTarget, dYOnTarget) Then
                    If ((dTempOffset >= oOrigin.Offset)) Then
                        Utilities.RecordError(corridorState, CorridorError.ValueInABadPosition, "Left Width Alignment", "CrownedLane")
                        Exit Sub
                    Else
                        vLeftLaneWidth = Abs(dTempOffset - oOrigin.Offset)
                    End If
                End If
            Catch
            End Try
            Try
                offsetRightTarget = oParamsOffsetTarget.Value("Right Width Alignment")
                Dim dTempOffset As Double
                If True = Utilities.CalcAlignmentOffsetToThisAlignment(oAlignmentId, _
                                                             oOrigin.Station, _
                                                             offsetRightTarget, _
                                                             dTempOffset, _
                                                             dXOnTarget, dYOnTarget) Then
                    If ((dTempOffset <= oOrigin.Offset)) Then
                        Utilities.RecordError(corridorState, CorridorError.ValueInABadPosition, "Right Width Alignment", "CrownedLane")
                        Exit Sub
                    Else
                        vRightLaneWidth = Abs(dTempOffset - oOrigin.Offset)
                    End If
                End If
            Catch
            End Try
            Try
                'According to spec, left side lane use left inside SE, right side use right inside SE
                dLeftSlope = oAlignment.GetCrossSlopeAtStation(oOrigin.Station, SuperelevationCrossSegmentType.LeftInLaneCrossSlope, True)
            Catch
            End Try
            Try
                'According to spec, left side lane use left inside SE, right side use right inside SE
                dRightSlope = oAlignment.GetCrossSlopeAtStation(oOrigin.Station, SuperelevationCrossSegmentType.RightInLaneCrossSlope, True)
            Catch
            End Try
        Else
        End If

        ' -------------------------------------------------------------------------------
        ' see if a profile was given for the Outside Elevation

        Dim elevationLeftTarget As SlopeElevationTarget 'slope or elvation target
        elevationLeftTarget = Nothing
        Dim elevationRightTarget As SlopeElevationTarget 'slope or elvation target
        elevationRightTarget = Nothing
        Dim dOutsideLeftElevation As Double
        Dim dOutsideRightElevation As Double

        If corridorState.Mode <> CorridorMode.Layout Then
            '*********************************************************
            'On Error Resume Next
            'Err.Clear()
            Try
                elevationLeftTarget = oParamsElevationTarget.Value("Left Outside Elevation")
            Catch
                elevationLeftTarget = Nothing
            End Try

            If Not elevationLeftTarget Is Nothing Then
                Try
                    dOutsideLeftElevation = elevationLeftTarget.GetElevation(oAlignmentId, oOrigin.Station)
                    dLeftSlope = (dOutsideLeftElevation - oOrigin.Elevation) / vLeftLaneWidth
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Left Outside Elevation", "CrownedLane")
                End Try
            End If

            Try
                elevationRightTarget = oParamsElevationTarget.Value("Right Outside Elevation")
            Catch
                elevationRightTarget = Nothing
            End Try

            If Not elevationRightTarget Is Nothing Then
                Try
                    dOutsideRightElevation = elevationRightTarget.GetElevation(oAlignmentId, oOrigin.Station)
                    dRightSlope = (dOutsideRightElevation - oOrigin.Elevation) / vRightLaneWidth
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Left Outside Elevation", "CrownedLane")
                End Try
            End If
        End If
        Dim dLeftBaseSlope, dRightBaseSlope As Double
        dLeftBaseSlope = vBaseLeftSlope
        dRightBaseSlope = vBaseRightSlope

        'whether or not use superelevation, we should do the following calculation
        dLeftBaseSlope = dLeftSlope + (vBaseLeftSlope - vDefaultLeftSlope)
        dRightBaseSlope = dRightSlope + (vBaseRightSlope - vDefaultRightSlope)


        Dim oParamDouble As ParamDouble
        oParamDouble = oParamsDouble.Add("Left Lane Slope", dLeftSlope)
        If Not oParamDouble Is Nothing Then
            oParamDouble.Access = ParamAccessType.Output
            oParamDouble.TypeInfo = TypeInfoDouble.TransparentCmdGrade
            oParamDouble.DisplayName = "11080"
        End If
        oParamDouble = oParamsDouble.Add("Right Lane Slope", dRightSlope)
        If Not oParamDouble Is Nothing Then
            oParamDouble.Access = ParamAccessType.Output
            oParamDouble.TypeInfo = TypeInfoDouble.TransparentCmdGrade
            oParamDouble.DisplayName = "11081"
        End If
        '*********************************************************
        'On Error GoTo ErrorHandler
        '---------------------------------------------------------------------
        'to compute all points' coordinate
        ' no P15
        Dim dPointOffsetHorizontalArray(0 To 16) As Double
        Dim dPointOffsetVerticalArray(0 To 16) As Double


        'P2 is the attachment point
        dPointOffsetHorizontalArray(2) = oOrigin.Offset - oOrigin.Offset
        dPointOffsetVerticalArray(2) = oOrigin.Elevation - oOrigin.Elevation

        dPointOffsetHorizontalArray(1) = dPointOffsetHorizontalArray(2) - vLeftLaneWidth
        'dLeftSlope is of numeric type, it will be a negative in this case
        dPointOffsetVerticalArray(1) = vLeftLaneWidth * dLeftSlope

        dPointOffsetHorizontalArray(3) = vRightLaneWidth
        'dRightSlope is of numeric type, it will be a negative in this case
        dPointOffsetVerticalArray(3) = vRightLaneWidth * dRightSlope


        dPointOffsetHorizontalArray(5) = dPointOffsetHorizontalArray(2)
        dPointOffsetVerticalArray(5) = dPointOffsetVerticalArray(2) - vPave1Depth

        dPointOffsetHorizontalArray(4) = dPointOffsetHorizontalArray(1)
        dPointOffsetVerticalArray(4) = dPointOffsetVerticalArray(1) - vPave1Depth

        dPointOffsetHorizontalArray(6) = dPointOffsetHorizontalArray(3)
        dPointOffsetVerticalArray(6) = dPointOffsetVerticalArray(3) - vPave1Depth

        dPointOffsetHorizontalArray(8) = dPointOffsetHorizontalArray(5)
        dPointOffsetVerticalArray(8) = dPointOffsetVerticalArray(5) - vPave2Depth

        dPointOffsetHorizontalArray(7) = dPointOffsetHorizontalArray(4)
        dPointOffsetVerticalArray(7) = dPointOffsetVerticalArray(4) - vPave2Depth

        dPointOffsetHorizontalArray(9) = dPointOffsetHorizontalArray(6)
        dPointOffsetVerticalArray(9) = dPointOffsetVerticalArray(6) - vPave2Depth

        dPointOffsetHorizontalArray(12) = dPointOffsetHorizontalArray(8) + dCrownOffset
        dPointOffsetVerticalArray(12) = dPointOffsetVerticalArray(8) - vBaseDepth

        dPointOffsetHorizontalArray(11) = dPointOffsetHorizontalArray(7)
        ' According to latest spec by Chakri, set zero to omit the layer
        If (vBaseDepth > 0.0) Then
            dPointOffsetVerticalArray(11) = dPointOffsetVerticalArray(7) - vBaseDepth + vLeftLaneWidth * (dLeftBaseSlope - dLeftSlope)
        Else
            dPointOffsetVerticalArray(11) = dPointOffsetVerticalArray(7)
        End If


        dPointOffsetHorizontalArray(13) = dPointOffsetHorizontalArray(9)
        If (vBaseDepth > 0.0) Then
            dPointOffsetVerticalArray(13) = dPointOffsetVerticalArray(9) - vBaseDepth + vRightLaneWidth * (dRightBaseSlope - dRightSlope)
        Else
            dPointOffsetVerticalArray(13) = dPointOffsetVerticalArray(9)
        End If

        dPointOffsetHorizontalArray(15) = dPointOffsetHorizontalArray(12)
        dPointOffsetVerticalArray(15) = dPointOffsetVerticalArray(12) - vSubbaseDepth

        dPointOffsetHorizontalArray(14) = dPointOffsetHorizontalArray(11)
        dPointOffsetVerticalArray(14) = dPointOffsetVerticalArray(11) - vSubbaseDepth

        dPointOffsetHorizontalArray(16) = dPointOffsetHorizontalArray(13)
        dPointOffsetVerticalArray(16) = dPointOffsetVerticalArray(13) - vSubbaseDepth




        '-------------------------------------------------------------------------------
        'Draw link and shape
        DrawLinkAndShape(corridorState, _
                         vPave1Depth, _
                         vPave2Depth, _
                         vBaseDepth, _
                         vSubbaseDepth, _
                         dPointOffsetHorizontalArray, _
                         dPointOffsetVerticalArray)

        oParamDouble = oParamsDouble.Add("LeftLaneWidth", vLeftLaneWidth)
        If Not oParamDouble Is Nothing Then
            oParamDouble.Access = ParamAccessType.InputAndOutput
            oParamDouble.DisplayName = "11005"
        End If
        oParamDouble = oParamsDouble.Add("RightLaneWidth", vRightLaneWidth)
        If Not oParamDouble Is Nothing Then
            oParamDouble.Access = ParamAccessType.InputAndOutput
            oParamDouble.DisplayName = "11007"
        End If

        'to indicate that this SA is using SE but doesn't support AOR calculation
        Utilities.SetSEAORUnsupportedTag(corridorState)

        '---------------------------------------------------------
        '---------------------------------------------------------
        ' Write back all the Calculated values of the input parameters into the RoadwayState object.
        ' Because they may be different from the default design values,
        ' we should write them back to make sure that the RoadwayState object
        ' contains the Actual information of the parameters.
        oParamsDouble.Add("LeftLaneWidth", vLeftLaneWidth)
        oParamsDouble.Add("RightLaneWidth", vRightLaneWidth)
        oParamsDouble.Add("DefaultLeftSlope", vDefaultLeftSlope)
        oParamsDouble.Add("DefaultRightSlope", vDefaultRightSlope)
        oParamsDouble.Add("BaseLeftSlope", vBaseLeftSlope)
        oParamsDouble.Add("BaseRightSlope", vBaseRightSlope)
        oParamsDouble.Add("Pave1Depth", vPave1Depth)
        oParamsDouble.Add("Pave2Depth", vPave2Depth)
        oParamsDouble.Add("BaseDepth", vBaseDepth)
        oParamsDouble.Add("SubBaseDepth", vSubbaseDepth)

    End Sub


    Private Sub DrawLinkAndShape(ByVal corridorState As CorridorState, _
                                 ByVal vPave1Depth As Double, _
                                 ByVal vPave2Depth As Double, _
                                 ByVal vBaseDepth As Double, _
                                 ByVal vSubBaseDepth As Double, _
                                 ByVal dPointOffsetHorizontalArray() As Double, _
                                 ByVal dPointOffsetVerticalArray() As Double)

        '------------------------------------------------------------------------------
        'Get Collection of points, links, shapes from corridorState
        Dim corridorPoints As PointCollection
        corridorPoints = corridorState.Points

        Dim oCorridorLinks As LinkCollection
        oCorridorLinks = corridorState.Links

        Dim corridorShapes As ShapeCollection
        corridorShapes = corridorState.Shapes

        Dim oCorridorPointArray(15) As Point
        Dim oCorridorLinkArray(13) As Link
        Dim oCorridorShapeArray(4) As Autodesk.Civil.DatabaseServices.Shape
        Dim oTempCorridorPointArray(0 To 1) As Point
        'special for the triple points link
        Dim oPolyLinkPointArray(0 To 2) As Point
        Dim oTempCorridorLinkArray(0 To 3) As Link
        'declare arrays to hold all points, links, shapes

        '-------------------------------------------------------------------------------

        oCorridorPointArray(1) = corridorPoints.Add(dPointOffsetHorizontalArray(1), dPointOffsetVerticalArray(1), Codes.ETW.Code)
        oCorridorPointArray(1).Codes.TryAdd(Codes.Lane.Code)
        oCorridorPointArray(2) = corridorPoints.Add(dPointOffsetHorizontalArray(2), dPointOffsetVerticalArray(2), Codes.Crown.Code)
        oCorridorPointArray(3) = corridorPoints.Add(dPointOffsetHorizontalArray(3), dPointOffsetVerticalArray(3), Codes.ETW.Code)
        oCorridorPointArray(3).Codes.TryAdd(Codes.Lane.Code)

        If vPave1Depth > 0.0# Then
            oCorridorPointArray(4) = corridorPoints.Add(dPointOffsetHorizontalArray(4), dPointOffsetVerticalArray(4), Codes.ETWPave1.Code)
            oCorridorPointArray(4).Codes.TryAdd(Codes.LanePave1.Code)
            oCorridorPointArray(6) = corridorPoints.Add(dPointOffsetHorizontalArray(6), dPointOffsetVerticalArray(6), Codes.ETWPave1.Code)
            oCorridorPointArray(6).Codes.TryAdd(Codes.LanePave1.Code)
            oCorridorPointArray(5) = corridorPoints.Add(dPointOffsetHorizontalArray(5), dPointOffsetVerticalArray(5), Codes.CrownPave1.Code)
        End If

        If vPave2Depth > 0.0# Then
            oCorridorPointArray(7) = corridorPoints.Add(dPointOffsetHorizontalArray(7), dPointOffsetVerticalArray(7), Codes.ETWPave2.Code)
            oCorridorPointArray(7).Codes.TryAdd(Codes.LanePave2.Code)
            oCorridorPointArray(9) = corridorPoints.Add(dPointOffsetHorizontalArray(9), dPointOffsetVerticalArray(9), Codes.ETWPave2.Code)
            oCorridorPointArray(9).Codes.TryAdd(Codes.LanePave2.Code)
            oCorridorPointArray(8) = corridorPoints.Add(dPointOffsetHorizontalArray(8), dPointOffsetVerticalArray(8), Codes.CrownPave2.Code)
        End If

        If vBaseDepth > 0.0# Then
            oCorridorPointArray(10) = corridorPoints.Add(dPointOffsetHorizontalArray(11), dPointOffsetVerticalArray(11), Codes.ETWBase.Code)
            oCorridorPointArray(10).Codes.TryAdd(Codes.LaneBase.Code)
            oCorridorPointArray(12) = corridorPoints.Add(dPointOffsetHorizontalArray(13), dPointOffsetVerticalArray(13), Codes.ETWBase.Code)
            oCorridorPointArray(12).Codes.TryAdd(Codes.LaneBase.Code)
            oCorridorPointArray(11) = corridorPoints.Add(dPointOffsetHorizontalArray(12), dPointOffsetVerticalArray(12), Codes.CrownBase.Code)
        End If

        If vSubBaseDepth > 0.0# Then
            oCorridorPointArray(13) = corridorPoints.Add(dPointOffsetHorizontalArray(14), dPointOffsetVerticalArray(14), Codes.LaneSub.Code)
            oCorridorPointArray(13).Codes.TryAdd(Codes.ETWSub.Code)
            oCorridorPointArray(15) = corridorPoints.Add(dPointOffsetHorizontalArray(16), dPointOffsetVerticalArray(16), Codes.LaneSub.Code)
            oCorridorPointArray(15).Codes.TryAdd(Codes.ETWSub.Code)
            oCorridorPointArray(14) = corridorPoints.Add(dPointOffsetHorizontalArray(15), dPointOffsetVerticalArray(15), Codes.CrownSub.Code)
        End If

        '-----------------------------------------------------------------------------
        ' all the links


        oPolyLinkPointArray(0) = oCorridorPointArray(1)
        oPolyLinkPointArray(1) = oCorridorPointArray(2)
        oPolyLinkPointArray(2) = oCorridorPointArray(3)
        oCorridorLinkArray(1) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Top.Code)
        oCorridorLinkArray(1).Codes.TryAdd(Codes.Pave.Code)
        oPolyLinkPointArray(0) = oCorridorPointArray(1)
        oPolyLinkPointArray(1) = oCorridorPointArray(2)
        oPolyLinkPointArray(2) = oCorridorPointArray(3)
        oCorridorLinkArray(1) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Top.Code)
        oCorridorLinkArray(1).Codes.TryAdd(Codes.Pave.Code)

        If vPave1Depth > 0.0# And vPave2Depth > 0.0# Then

            oPolyLinkPointArray(0) = oCorridorPointArray(6)
            oPolyLinkPointArray(1) = oCorridorPointArray(5)
            oPolyLinkPointArray(2) = oCorridorPointArray(4)
            oCorridorLinkArray(2) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave1.Code)

            oPolyLinkPointArray(0) = oCorridorPointArray(7)
            oPolyLinkPointArray(1) = oCorridorPointArray(8)
            oPolyLinkPointArray(2) = oCorridorPointArray(9)
            oCorridorLinkArray(3) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave2.Code)

            If vBaseDepth > 0.0# And vSubBaseDepth > 0.0# Then

                oPolyLinkPointArray(0) = oCorridorPointArray(12)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(10)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(13)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(15)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(13)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(15)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(8)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(9)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave2.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(11)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(10)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(12)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(13)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)


            ElseIf (vBaseDepth > 0.0#) Then

                oPolyLinkPointArray(0) = oCorridorPointArray(12)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(10)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(8)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(9)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave2.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(11)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(10)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)


            ElseIf (vSubBaseDepth > 0.0#) Then


                oPolyLinkPointArray(0) = oCorridorPointArray(15)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(13)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(13)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(15)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(8)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(9)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave2.Code)


                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(13)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(12)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)

            Else

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(7)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(9)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(8)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(9)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave2.Code)

            End If

        ElseIf vPave1Depth > 0.0# Then

            oPolyLinkPointArray(0) = oCorridorPointArray(6)
            oPolyLinkPointArray(1) = oCorridorPointArray(5)
            oPolyLinkPointArray(2) = oCorridorPointArray(4)
            oCorridorLinkArray(2) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave1.Code)

            If vBaseDepth > 0.0# And vSubBaseDepth > 0.0# Then

                oPolyLinkPointArray(0) = oCorridorPointArray(10)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(12)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(15)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(13)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(13)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(15)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(10)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(11)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(13)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(12)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)


            ElseIf (vBaseDepth > 0.0#) Then

                oPolyLinkPointArray(0) = oCorridorPointArray(10)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(12)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)


                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(10)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(11)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

            ElseIf (vSubBaseDepth > 0.0#) Then

                oPolyLinkPointArray(0) = oCorridorPointArray(13)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(15)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(13)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(15)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(12)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(13)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)

            Else

                oTempCorridorPointArray(0) = oCorridorPointArray(4)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(6) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(6)
                oCorridorLinkArray(7) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(7)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(2)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(6)
                oCorridorShapeArray(1) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

            End If

        ElseIf vPave2Depth > 0.0# Then

            If vBaseDepth > 0.0# And vSubBaseDepth > 0.0# Then

                oPolyLinkPointArray(0) = oCorridorPointArray(9)
                oPolyLinkPointArray(1) = oCorridorPointArray(8)
                oPolyLinkPointArray(2) = oCorridorPointArray(7)
                oCorridorLinkArray(3) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave2.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(10)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(12)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(15)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(13)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(13)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(15)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(9)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(8)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(10)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(11)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(13)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(12)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)


            ElseIf (vBaseDepth > 0.0#) Then
                oPolyLinkPointArray(0) = oCorridorPointArray(9)
                oPolyLinkPointArray(1) = oCorridorPointArray(8)
                oPolyLinkPointArray(2) = oCorridorPointArray(7)
                oCorridorLinkArray(3) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave2.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(10)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(12)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(10)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(12)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(9)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(8)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(10)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(11)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

            ElseIf (vSubBaseDepth > 0.0#) Then
                oPolyLinkPointArray(0) = oCorridorPointArray(9)
                oPolyLinkPointArray(1) = oCorridorPointArray(8)
                oPolyLinkPointArray(2) = oCorridorPointArray(7)
                oCorridorLinkArray(3) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave2.Code)


                oPolyLinkPointArray(0) = oCorridorPointArray(13)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(15)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(4)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(6)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(13)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(15)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(9)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(8)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)


                oTempCorridorLinkArray(0) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(12)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(13)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)

            Else
                oPolyLinkPointArray(0) = oCorridorPointArray(9)
                oPolyLinkPointArray(1) = oCorridorPointArray(8)
                oPolyLinkPointArray(2) = oCorridorPointArray(7)
                oCorridorLinkArray(3) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Pave2.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(7)
                oTempCorridorPointArray(1) = oCorridorPointArray(4)
                oCorridorLinkArray(8) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(6)
                oTempCorridorPointArray(1) = oCorridorPointArray(9)
                oCorridorLinkArray(9) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes
                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(9)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(3)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(8)
                oCorridorShapeArray(2) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Pave1.Code)
            End If

        Else
            If vBaseDepth > 0.0# And vSubBaseDepth > 0.0# Then

                oPolyLinkPointArray(0) = oCorridorPointArray(12)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(10)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oPolyLinkPointArray(0) = oCorridorPointArray(13)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(15)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(13)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(15)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(11)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(10)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

                oTempCorridorLinkArray(0) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(12)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(13)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)


            ElseIf (vBaseDepth > 0.0#) Then
                oPolyLinkPointArray(0) = oCorridorPointArray(12)
                oPolyLinkPointArray(1) = oCorridorPointArray(11)
                oPolyLinkPointArray(2) = oCorridorPointArray(10)
                oCorridorLinkArray(4) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.Base.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(10)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(10) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(12)
                oCorridorLinkArray(11) = oCorridorLinks.Add(oTempCorridorPointArray, "")


                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(11)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(4)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(10)
                oCorridorShapeArray(3) = corridorShapes.Add(oTempCorridorLinkArray, Codes.Base.Code)

            ElseIf (vSubBaseDepth > 0.0#) Then


                oPolyLinkPointArray(0) = oCorridorPointArray(15)
                oPolyLinkPointArray(1) = oCorridorPointArray(14)
                oPolyLinkPointArray(2) = oCorridorPointArray(13)
                oCorridorLinkArray(5) = oCorridorLinks.Add(oPolyLinkPointArray, Codes.SubBase.Code)
                oCorridorLinkArray(5).Codes.TryAdd(Codes.Datum.Code)

                oTempCorridorPointArray(0) = oCorridorPointArray(13)
                oTempCorridorPointArray(1) = oCorridorPointArray(1)
                oCorridorLinkArray(12) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                oTempCorridorPointArray(0) = oCorridorPointArray(3)
                oTempCorridorPointArray(1) = oCorridorPointArray(15)
                oCorridorLinkArray(13) = oCorridorLinks.Add(oTempCorridorPointArray, "")

                ' now deal with shapes

                oTempCorridorLinkArray(0) = oCorridorLinkArray(1)
                oTempCorridorLinkArray(1) = oCorridorLinkArray(13)
                oTempCorridorLinkArray(2) = oCorridorLinkArray(5)
                oTempCorridorLinkArray(3) = oCorridorLinkArray(12)
                oCorridorShapeArray(4) = corridorShapes.Add(oTempCorridorLinkArray, Codes.SubBase.Code)

            Else
                'no link and shape added

            End If
        End If
    End Sub

End Class

