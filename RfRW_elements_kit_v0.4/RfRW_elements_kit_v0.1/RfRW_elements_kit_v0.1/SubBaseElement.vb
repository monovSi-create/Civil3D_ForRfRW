Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.Internal
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil.Runtime
Imports Autodesk.AutoCAD.DatabaseServices
Imports System.Math
Imports Autodesk.AutoCAD.Geometry
Imports System.ComponentModel.Design
Imports Autodesk.Civil.DatabaseServices.Styles
Imports System.Linq
Imports Autodesk.Aec.DatabaseServices
Public Class SubBaseElement
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: 
    '
    '   Description: Creates a simple cross-sectional representation of foundation for facing elements.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetSurface              Surface    Yes       May be used to judge fill/cut condition
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                Сторона                   long        no          Right            specifies side to place SA on
    '                ТолщинаСлоя               double      no          0.3              width of geogrids
    '                ШиринаПризмы              double      no          3.0              step of geogrid layer
    '                ОтступОтОси               double      no          1.0              0
    '                Насыпь/Выемка             bool        no           3               0
    '                Кол-во слоев              long        no           2               0
    '                ШагФундаментов            double      no          0.5              step of geogrid layer
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None

    Private Const SideDefault = Utilities.Right  '"right"
    Private Const WidthDefault = 3.0
    Private Const HeightDefault = 0.3
    Private Const dSlopeOffset = 1.0
    Private Const dSlopeType = Utilities.SubBaseDirection.Выемка
    Private Const dGeogridCount = 2
    Private Const dFoundatStep = 0.5
    Private Const dAssemblyNameDefault = "Участок"
    Private Const dStepOffset = 0.3
    Private Const dStepSlope = 1

    Protected Overrides Sub GetInputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)
        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        ' define collection for string parameters in corridor
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        Dim paramsBool As ParamBoolCollection
        paramsBool = corridorState.ParamsBool

        ' Add input parameters we used in this script
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsDouble.Add("Ширина призмы", WidthDefault)
        paramsDouble.Add("Толщина слоя", HeightDefault)
        paramsDouble.Add("Отступ от оси", dSlopeOffset)
        paramsBool.Add("Насыпь/Выемка", dSlopeType)
        paramsLong.Add("Количество слоев", dGeogridCount)
        paramsDouble.Add("Шаг фундаментов", dFoundatStep)
        paramsDouble.Add("Отступ ступени вдоль оси", dStepOffset)
        paramsDouble.Add("Заложение откоса", dStepSlope)
    End Sub
    Protected Overrides Sub GetLogicalNamesImplement(ByVal corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)
        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim paramLong As ParamLong
        'paramLong = paramsLong.Add("Профиль дна котлована", ParamLogicalNameType.ElevationTarget)
        paramLong = paramsLong.Add("offsetTarget", ParamLogicalNameType.OffsetTarget)
        paramLong.DisplayName = "Тыльная граница щебеночной подготовки"

    End Sub

    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)
        ' Retrieve parameter buckets from the corridor state

    End Sub

    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        'Dim oParamsSurface As ParamSurfaceCollection
        'oParamsSurface = corridorState.ParamsSurface

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget

        'Dim oIntersectionPointWithSurface As IPoint = Nothing

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsBool As ParamBoolCollection
        paramsBool = corridorState.ParamsBool

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        '-----------------------------------------
        ' on error resume next
        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '----------------------------------------
        'flip about Y axis
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '----------------------------------------
        'foundation dimensions
        Dim oWidth As Double
        Try
            oWidth = paramsDouble.Value("Ширина призмы")
        Catch
            oWidth = WidthDefault
        End Try
        '-----------------------------------------
        Dim oHeight As Double
        Try
            oHeight = paramsDouble.Value("Толщина слоя")
        Catch
            oHeight = HeightDefault
        End Try
        '----------------------------------------
        Dim oSOffset As Double
        Try
            oSOffset = paramsDouble.Value("Отступ от оси")
        Catch
            oSOffset = dSlopeOffset
        End Try
        '----------------------------------------
        Dim oSType As Boolean
        Try
            oSType = paramsBool.Value("Насыпь/Выемка")
        Catch
            oSType = dSlopeType
        End Try
        '----------------------------------------
        Dim oGCount As Long
        Try
            oGCount = paramsLong.Value("Количество слоев")
        Catch
            oGCount = dGeogridCount
        End Try
        '----------------------------------------
        Dim oFStep As Double
        Try
            oFStep = paramsDouble.Value("Шаг фундаментов")
        Catch
            oFStep = dFoundatStep
        End Try
        '----------------------------------------
        Dim oStepOffset As Double
        Try
            oStepOffset = paramsDouble.Value("Отступ ступени вдоль оси")
        Catch
            oStepOffset = dStepOffset
        End Try
        '----------------------------------------
        Dim oStepSlope As Double
        Try
            oStepSlope = paramsDouble.Value("Заложение откоса")
        Catch
            oStepSlope = dStepSlope
        End Try

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As Autodesk.AutoCAD.DatabaseServices.ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        Dim gridName As String = "Триакс"
        Dim solidName As String = "Щебень основания"
        Dim pitName As String = "Котлован"
        Dim slopeName As String = "Откосы"
        'Dim maxH = oGCount * oHeight + oFStep
        'Dim stepToLower = oHeight - (oGCount * oHeight - oFStep)
        Dim startStep As Boolean
        Dim endStep As Boolean
        Dim beforeNameRegion As String
        Dim afterNameRegion As String
        Dim tangSlope As Double = 1 / oStepSlope
        Dim subHeight As Double = oGCount * oHeight

        If corridorState.Mode <> CorridorMode.Layout Then
            'Определяем глубину отступа для щебеночной подушки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("offsetTarget")
            Catch
                offsetTarget = Nothing
            End Try

            Dim hasOffsetTarget As Boolean
            hasOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim baseOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, baseOffset, xOffset, yOffset)
                    hasOffsetTarget = True
                    oWidth = baseOffset - oOrigin.Offset + oSOffset * flip
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "offsetTarget", "RetainWallHorizontal")
                End Try
            End If

            'Проверяем наличие ступеней в начеле и конце участка
            BaseSteps(corridorState, oFStep, startStep, endStep, beforeNameRegion, afterNameRegion)
            'Добавляем при необходимости сечения для перехода 
            If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                AddStations(tm, corridorState, oStepOffset, tangSlope, startStep, endStep, oFStep, oGCount, oHeight)
            End If
            'создание поперечных сечений в зависимости от входных условий
            CreateWithSteps(corridorState,
            oStepOffset,
            tangSlope,
            subHeight,
            oFStep,
            oGCount,
            flip,
            oSOffset,
            oWidth,
            oHeight,
            startStep,
            endStep,
            gridName,
            solidName,
            pitName,
            slopeName,
            beforeNameRegion,
            afterNameRegion, hasOffsetTarget
            )
            '    'получаем расстояние до профиля

        Else 'for layout mode
            Dim H As Double = oGCount * oHeight
            StandartSubBase(corridorState, oGCount, flip, oSOffset, oWidth, oHeight, H, tangSlope, gridName, solidName, pitName, slopeName, False)
        End If

        '------------------------------------------------------------------------
        Dim param As IParam

        param = paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Ширина призмы", WidthDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Толщина слоя", HeightDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Отступ от оси", dSlopeOffset)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsBool.Add("Насыпь/Выемка", dSlopeType)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsLong.Add("Количество слоев", dGeogridCount)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Шаг фундаментов", dFoundatStep)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Отступ ступени вдоль оси", dStepOffset)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Заложение откоса", dStepSlope)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
    End Sub
    Sub BaseSteps(corridorState As CorridorState, deltaHeight As Double, ByRef startStep As Boolean, ByRef endStep As Boolean, ByRef beforeRegName As String, ByRef afterRegName As String) 'метод для анализа необходимости ступеней
        'извлекаем осевую линию
        Dim oPnt As PointInMem
        If oPnt Is Nothing Then oPnt = New PointInMem

        Dim tm As DBTransactionManager = HostApplicationServices.WorkingDatabase.TransactionManager

        Dim oProfile = TryCast(tm.GetObject(corridorState.CurrentProfileId, OpenMode.ForRead), Profile)

        Dim oOrigin As New PointInMem
        Dim oAlignmentId As Autodesk.AutoCAD.DatabaseServices.ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oAlignmentId, oOrigin)
        'задаем расстояния для анализа
        Dim currRegStart = corridorState.CurrentRegionStartStation
        Dim currRegEnd = corridorState.CurrentRegionEndStation
        Dim currStat = corridorState.CurrentStation
        Dim beforeReg = currRegStart
        Dim afterReg = currRegEnd

        oPnt.Station = currRegStart


        Try
            beforeReg = currRegStart - 0.01
        Catch

        End Try

        Try
            afterReg = currRegEnd + 0.01
        Catch

        End Try
        'получаем координаты точек на заданных расстояниях

        Dim elevBefore = oProfile.ElevationAt(beforeReg)
        Dim elevStart = oProfile.ElevationAt(currRegStart)
        Dim elevCurr = oProfile.ElevationAt(currStat)
        Dim elevEnd = oProfile.ElevationAt(currRegEnd)
        Dim elevAfter = oProfile.ElevationAt(afterReg)

        If elevBefore < elevStart Then
            startStep = True
        Else
            startStep = False
        End If
        If elevAfter < elevEnd Then
            endStep = True
        Else
            endStep = False
        End If
        'находим имя предыдущего и следующего участка (необходимо для корректного построения характерных линий по точкам)
        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        For Each baseline As Baseline In baselines
            If corridorState.CurrentProfileId = baseline.ProfileId Then
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs

                    If reg.EndStation = currRegStart - 0.001 Then
                        beforeRegName = reg.Name
                    End If
                    If reg.StartStation = currRegEnd + 0.001 Then
                        afterRegName = reg.Name

                    End If
                Next
            End If
            Exit For
        Next

    End Sub
    Sub CreateWithSteps(corridorState As CorridorState,
                    offset As Double,
                    slope As Double,
                    subHeight As Double,
                    fStep As Double,
                    oGCount As Long,
                    flip As Double,
                    oSOffset As Double,
                    oWidth As Double,
                    oHeight As Double,
                    startStep As Boolean,
                    endStep As Boolean,
                    gridName As String,
                    solidName As String,
                    pitName As String,
                    slopeName As String,
                    beforeRegName As String,
                    afterRegName As String,
                    hasTarget As Boolean
                    )

        Dim maxH = subHeight + fStep
        Dim l2 = fStep / slope
        Dim currRegStart = corridorState.CurrentRegionStartStation
        Dim currRegEnd = corridorState.CurrentRegionEndStation
        Dim currStat = corridorState.CurrentStation
        'начальная точка вставки конструкции



        If currStat >= currRegStart And currStat <= currRegStart + offset And startStep Then 'блок для первого варианта конструкции (в начале участка)
            Dim H = subHeight + fStep
            MaxSubBase(corridorState,
                    oGCount,
                    flip,
                    oSOffset,
                    oWidth,
                    oHeight,
                    fStep,
                    H,
                    slope,
                    gridName,
                    solidName,
                    pitName,
                    slopeName,
                    beforeRegName, hasTarget)
        ElseIf currStat > currRegStart + offset And currStat < currRegStart + offset + l2 And startStep Then 'блок для второго варианта конструкции (в начале участка)
            Dim dState = currStat - (currRegStart + offset)
            Dim H = maxH - dState * slope
            StepSubBase(corridorState,
                        oGCount,
                        flip,
                        oSOffset,
                        oWidth,
                        oHeight,
                        fStep,
                        H,
                        slope,
                        gridName,
                        solidName,
                        pitName,
                        slopeName,
                        beforeRegName, hasTarget)
        ElseIf currStat < currRegEnd - offset And currStat > currRegEnd - offset - l2 And endStep Then 'блок для второго варианта конструкции (в конце участка)
            Dim dState = currStat - (currRegEnd - (offset + l2))
            Dim H = subHeight + dState * slope
            StepSubBase(corridorState,
                            oGCount,
                            flip,
                            oSOffset,
                            oWidth,
                            oHeight,
                            fStep,
                            H,
                            slope,
                            gridName,
                            solidName,
                            pitName,
                            slopeName,
                            afterRegName, hasTarget)
        ElseIf currStat <= currRegEnd And currStat >= currRegEnd - offset And endStep Then 'блок для первого варианта конструкции (в конце участка)
            Dim H = subHeight + fStep
            MaxSubBase(corridorState,
                        oGCount,
                        flip,
                        oSOffset,
                        oWidth,
                        oHeight,
                        fStep,
                        H,
                        slope,
                        gridName,
                        solidName,
                        slopeName,
                        pitName,
                        afterRegName, hasTarget)
        Else 'для варианта конструкции без ступеней 
            Dim H = subHeight
            StandartSubBase(corridorState,
                        oGCount,
                        flip,
                        oSOffset,
                        oWidth,
                        oHeight,
                        H,
                        slope,
                        gridName,
                        solidName,
                        pitName,
                        slopeName, hasTarget)
        End If
    End Sub
    Sub StandartSubBase(corridorState As CorridorState,
                        oGCount As Long,
                        flip As Double,
                        oSOffset As Double,
                        oWidth As Double,
                        oHeight As Double,
                        tHeight As Double,
                        slope As Double,
                        gridName As String,
                        solidName As String,
                        pitName As String,
                        slopeName As String,
                        hasTarget As Boolean)

        Dim inputPoint As New PointInMem With {
            .Offset = -oSOffset * flip,
            .Elevation = -tHeight}
        subGravForm(corridorState, flip, slope, tHeight, oWidth, solidName, pitName, hasTarget, inputPoint)
        Dim i As Integer = 1
        Dim gridWidth As Double = oWidth
        Do While i <= oGCount
            createGeogrid(corridorState, gridName, gridWidth, flip, hasTarget, inputPoint)
            inputPoint.Offset -= oHeight / slope * flip
            inputPoint.Elevation += oHeight
            If hasTarget Then
                gridWidth += oHeight / slope * flip
            Else
                gridWidth += oHeight / slope * 2
            End If
            i += 1
        Loop
    End Sub

    Sub StepSubBase(corridorState As CorridorState,
                        oGCount As Long,
                        flip As Double,
                        oSOffset As Double,
                        oWidth As Double,
                        oHeight As Double,
                        oFStep As Double,
                        tHeight As Double,
                        slope As Double,
                        gridName As String,
                        solidName As String,
                        pitName As String,
                        slopeName As String,
                        otherRegName As String,
                        hasTarget As Boolean)
        Dim maxHeight = oGCount * oHeight + oFStep
        Dim stepToLower = oHeight - (oGCount * oHeight - oFStep)
        Dim inputPoint As New PointInMem With {
            .Offset = -oSOffset * flip,
            .Elevation = -tHeight}

        subGravForm(corridorState, flip, slope, tHeight, oWidth, solidName, pitName, hasTarget, inputPoint) 'создаем общую фигуру щебеночной подушки
        Dim gridWidth As Double = oWidth
        If tHeight >= (maxHeight - oHeight) Then  'если сечение глубже щебеночной прослойки между матрасами
            'создаем доп. слой георешетки
            Dim lowGridH = maxHeight - oHeight 'высота нижней герешетки
            inputPoint.Offset -= Math.Abs(tHeight - lowGridH) / slope * flip
            inputPoint.Elevation = -lowGridH
            If hasTarget Then
                gridWidth = oWidth + Math.Abs(tHeight - lowGridH) / slope * flip
            Else
                gridWidth = oWidth + Math.Abs(tHeight - lowGridH) / slope * 2
            End If
            createGeogrid(corridorState, gridName, gridWidth, flip, hasTarget, inputPoint)

        End If
        inputPoint.Offset = -(oSOffset * flip + (tHeight - oGCount * oHeight) / slope * flip)
        inputPoint.Elevation = -oGCount * oHeight
        If hasTarget Then
            gridWidth = oWidth + (tHeight - oGCount * oHeight) / slope * flip
        Else
            gridWidth = oWidth + (tHeight - oGCount * oHeight) / slope * 2
        End If
        Dim i As Integer = 1
        Do While i <= oGCount 'верхние слои георешетки для "двойного матраса"
            createGeogrid(corridorState, gridName, gridWidth, flip, hasTarget, inputPoint)
            inputPoint.Offset -= oHeight / slope * flip
            inputPoint.Elevation += oHeight
            If hasTarget Then
                gridWidth += oHeight / slope * flip
            Else
                gridWidth += oHeight / slope * 2
            End If
            i += 1
        Loop

    End Sub
    Private Sub MaxSubBase(corridorState As CorridorState,
                        oGCount As Long,
                        flip As Double,
                        oSOffset As Double,
                        oWidth As Double,
                        oHeight As Double,
                        oFStep As Double,
                        tHeight As Double,
                        slope As Double,
                        gridName As String,
                        solidName As String,
                        pitName As String,
                        slopeName As String,
                        otherRegName As String,
                        hasTarget As Boolean)
        Dim gridWidth As Double = oWidth
        Dim stepToLower = oHeight - (oGCount * oHeight - oFStep)
        Dim inputPoint As New PointInMem With {
            .Offset = -oSOffset * flip,
            .Elevation = -tHeight}

        subGravForm(corridorState, flip, slope, tHeight, gridWidth, solidName, pitName, hasTarget, inputPoint)
            Dim i As Integer = 1
            Do While i <= oGCount 'нижние слои георешетки для "двойного матраса"
            createGeogrid(corridorState, gridName, gridWidth, flip, hasTarget, inputPoint)
            inputPoint.Offset -= oHeight / slope * flip
                inputPoint.Elevation += oHeight
                If hasTarget Then
                gridWidth += oHeight / slope * flip
            Else
                gridWidth += oHeight / slope * 2
            End If
                i += 1
            Loop
            inputPoint.Offset += oHeight / slope * flip
            inputPoint.Elevation -= oHeight

        inputPoint.Offset -= stepToLower * flip
        inputPoint.Elevation += stepToLower
        If hasTarget Then
            gridWidth += (-oHeight + stepToLower) / slope * flip
        Else
            gridWidth += (-oHeight + stepToLower) / slope * 2
        End If
        i = 1
        Do While i <= oGCount 'верхние слои георешетки для "двойного матраса"
            createGeogrid(corridorState, gridName, gridWidth, flip, hasTarget, inputPoint)
            inputPoint.Offset -= oHeight / slope * flip
            inputPoint.Elevation += oHeight
            If hasTarget Then
                gridWidth += oHeight / slope * flip
            Else
                gridWidth += oHeight / slope * 2
            End If
            i += 1
        Loop
    End Sub

    Sub AddStations(tm As DBTransactionManager,
                    corridorState As CorridorState,
                    stepOffset As Double,
                    stepSlope As Double,
                    startStep As Boolean,
                    endStep As Boolean,
                    foundationStep As Double,
                    gridCount As Integer,
                    layerHeight As Double)
        'объявляем необходимые пикеты с дополнительными сечениями
        'для ступени в начале рассматриваемой области
        Dim startLowerSt As Double
        Dim startUpperSt As Double
        Dim startMidSt As New List(Of Double)
        'для ступени в конце рассматриваемой области
        Dim endLowerSt As Double
        Dim endUpperSt As Double
        Dim endMidSt As New List(Of Double)

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs

                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "доп.сечения для ступеней матраса " + baseline.Name
                            If info.Description = description Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next

                        If startStep Then
                            startLowerSt = corridorState.CurrentRegionStartStation + stepOffset
                            Dim plusSect1 = startLowerSt + 0.01
                            startUpperSt = startLowerSt + foundationStep / stepSlope - 0.01
                            Dim plusSect2 = startUpperSt - 0.01
                            'набираем точки в уровнях георешеток
                            Dim i As Integer
                            i = 1
                            Do While i < gridCount
                                Dim m As Double
                                m = startLowerSt + i * layerHeight / stepSlope - 0.01
                                startMidSt.Add(m)
                                i += 1
                            Loop
                            startMidSt.Insert(0, startLowerSt)
                            startMidSt.Add(startUpperSt)
                            startMidSt.Add(plusSect1)
                            startMidSt.Add(plusSect2)
                            'если в точке нет сечения - создаем дополнительное
                            Dim assemblyStations As Double()
                            assemblyStations = reg.AppliedAssemblies.Stations

                            Dim diff = startMidSt.Except(assemblyStations)
                            For Each station In diff
                                Try
                                    reg.AddStation(station, "доп.сечения для ступеней матраса " + baseline.Name)
                                Catch

                                End Try
                            Next
                        End If
                        If endStep Then
                            endLowerSt = corridorState.CurrentRegionEndStation - stepOffset
                            Dim plusSect3 = endLowerSt - 0.01
                            endUpperSt = endLowerSt - foundationStep / stepSlope + 0.01
                            Dim plusSect4 = endUpperSt + 0.01
                            'набираем точки в уровнях георешеток
                            Dim i As Integer
                            i = 1
                            Do While i < gridCount
                                Dim m As Double
                                m = endLowerSt - i * layerHeight / stepSlope + 0.01
                                endMidSt.Add(m)
                                i += 1
                            Loop
                            endMidSt.Insert(0, endLowerSt)
                            endMidSt.Add(endUpperSt)
                            endMidSt.Add(plusSect3)
                            endMidSt.Add(plusSect4)
                            'если в точке нет сечения - создаем дополнительное
                            Dim assemblyStations As Double()
                            assemblyStations = reg.AppliedAssemblies.Stations

                            Dim diff = endMidSt.Except(assemblyStations)
                            For Each station In diff
                                Try
                                    reg.AddStation(station, "доп.сечения для ступеней матраса " + baseline.Name)
                                Catch

                                End Try
                            Next
                        End If
                    End If
                Next
            End If
        Next
    End Sub
    'создание фигуры щебеночной подготовки
    Private Sub subGravForm(corridorState As CorridorState, flip As Double, slope As Double, height As Double, width As Double, solidName As String, pitName As String, hasTarget As Boolean, inputPoint As PointInMem)
        'объявляем коллекции точек, связей и форм
        Dim gravPoints As PointCollection
        gravPoints = corridorState.Points
        Dim gravLinks As LinkCollection
        gravLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        Dim gravP1 As Point = gravPoints.Add(inputPoint.Offset, inputPoint.Elevation, pitName)
        Dim gravP2 As Point
        Dim gravP3 As Point
        If hasTarget Then
            gravP2 = gravPoints.Add(gravP1.Offset + width, gravP1.Elevation, pitName)
            gravP3 = gravPoints.Add(gravP2.Offset, gravP2.Elevation + height, solidName)
        Else
            gravP2 = gravPoints.Add(gravP1.Offset + flip * width, gravP1.Elevation, pitName)
            gravP3 = gravPoints.Add(gravP2.Offset + flip * height / slope, gravP2.Elevation + height, solidName)
        End If
        Dim gravP4 As Point = gravPoints.Add(gravP1.Offset - flip * height / slope, gravP1.Elevation + height, solidName)
        Dim gravLink1 As Link = gravLinks.Add(gravP1, gravP2, "")
        Dim gravLink2 As Link = gravLinks.Add(gravP2, gravP3, "")
        Dim gravLink3 As Link = gravLinks.Add(gravP3, gravP4, solidName)
        Dim gravLink4 As Link = gravLinks.Add(gravP4, gravP1, "")
        Dim gravShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, solidName)
    End Sub
    'создание георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
                              ByVal hasTarget As Boolean,
                              ByVal pointToInsert As PointInMem)
        '---------------------------------------------------------
        ' создание точек и связи между ними
        '---------------------------------------------------------
        Dim geogridPoints As PointCollection
        geogridPoints = corridorState.Points

        Dim geogridLinks As LinkCollection
        geogridLinks = corridorState.Links

        Dim gridPoint1 As Point
        Dim gridPoint2 As Point
        Dim gridLink As Link

        Dim gridF1 As String = linkName + "_1"
        Dim gridF2 As String = linkName + "_2"
        gridPoint1 = geogridPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, gridF1)
        If hasTarget Then
            gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth, pointToInsert.Elevation, gridF2)
        Else
            gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth * flipValue, pointToInsert.Elevation, gridF2)
        End If
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
End Class
