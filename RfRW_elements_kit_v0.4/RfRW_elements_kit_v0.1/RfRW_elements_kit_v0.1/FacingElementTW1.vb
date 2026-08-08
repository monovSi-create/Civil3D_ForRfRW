Option Strict Off
Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode

Public Class FacingElementTW1
    Inherits SATemplate
    Private Const dBlockWidth = 0.22
    Private Const dBlocksInLayout = 5
    Private Const dBlockHeight = 0.15
    Private Const deltaH = 0.002
    Private Const dBlockOffset = 0.0104
    Private Const dBlockLength = 0.4
    Private Const SideDefault = Utilities.Right
    Private Const dAssemblyNameDefault = "Участок"

    Private Shared _blocksCount As Integer = 0 'необходима для хранения значения на протяжении перестроения всего коридора

    Protected Overrides Sub GetInputParametersImplement(corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)

        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        Dim oParam As IParam
        ' Add input parameters we used in this script
        oParam = paramsLong.Add(Utilities.Side, SideDefault)
        oParam = paramsString.Add("AssemblyName", dAssemblyNameDefault)
        oParam = paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        oParam = paramsDouble.Add("BlocksDeltaH", deltaH)
        oParam = paramsDouble.Add("BlockLength", dBlockLength)
    End Sub
    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Проектный профиль", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"
        ParamLong = paramsLong.Add("blocksTop", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных блоков"
    End Sub
    Protected Overrides Sub GetOutputParametersImplement(corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

        ' Регистрируем выходной параметр BlocksCount
        Dim paramsLong As ParamLongCollection = corridorState.ParamsLong
        Dim paramsDouble As ParamDoubleCollection = corridorState.ParamsDouble
        Dim oParam As IParam

        oParam = paramsLong.Add("BlocksCount", _blocksCount)
        If oParam IsNot Nothing Then oParam.Access = ParamAccessType.Output

        oParam = paramsDouble.Add("BlockHeight", dBlockHeight)
        If oParam IsNot Nothing Then oParam.Access = ParamAccessType.Output
    End Sub

    Protected Overrides Sub DrawImplement(corridorState As CorridorState)
        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble
        '-----------------------------------------
        ' on error resume next
#Region "переменные"
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
        Dim blockLayers As Long
        Try
            blockLayers = paramsLong.Value("BlocksInLayout")
        Catch
            blockLayers = dBlocksInLayout
        End Try

        Dim dH As Double
        Try
            dH = paramsDouble.Value("BlocksDeltaH")
        Catch
            dH = deltaH
        End Try

        Dim dL As Double
        Try
            dL = paramsDouble.Value("BlockLength")
        Catch
            dL = dBlockLength
        End Try

#End Region
        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        If corridorState.Mode <> CorridorMode.Layout Then
            '------------------------
            'проводим анализ сечения
            '------------------------
            'определяем профиль для вычисления высоты стены
            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Проектный профиль")
            Catch
                elevationTarget = Nothing
            End Try
            Dim hasWallHeightProfile As Boolean
            hasWallHeightProfile = False
            Dim dWallHeight As Double

            If Not elevationTarget Is Nothing Then
                'получим высоту по профилю
                Try
                    dWallHeight = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallHeightProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If
            'определяем профиль облицовочных блоков (если имеется)
            'Dim blocksElevTarget As SlopeElevationTarget
            'Try
            '    blocksElevTarget = oParamsElevationTarget.Value("blocksTop")
            'Catch
            '    blocksElevTarget = Nothing
            'End Try
            '
            'Dim hasWallBlocksProfile As Boolean
            'hasWallBlocksProfile = False
            'Dim blocksHeight As Double
            Dim newOrigin As New PointInMem With {
                .Offset = 0,
                .Elevation = 0
                }
            Dim blockStep = dBlockHeight + dH
            'If Not blocksElevTarget Is Nothing Then
            '    'получим высоту по профилю
            '    Try
            '        blocksHeight = blocksElevTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
            '        hasWallBlocksProfile = True
            '    Catch
            '        Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
            '    End Try
            '    'сечение по заданному профилю высоты блоков+проектному
            '    Dim rows As Integer = CType(blocksHeight / blockStep, Integer)
            '    Dim levelWidth = 0.21
            '    createAddStationsForProfile(tm, corridorState, blocksElevTarget)
            '    createFacingTW(corridorState, dWallHeight, flip, rows, dH, levelWidth, newOrigin)
            'Else
            'в начале каждого региона(области)
            If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, blockStep, dL, elevationTarget)
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = blockStep * 1000
                    _blocksCount = dWallHeight * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 2
                        If isStep(tm, corridorState, firstTop) Then
                            _blocksCount -= 1
                        End If
                        firstTop += 0.001
                    End While
                End If

                'условие для переопределения высоты облицовки
                If isStep(tm, corridorState, corridorState.CurrentStation) And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation + 0.001 Then
                    'вспомогательные вектора до и после скачка для оценки направления проектного профиля
                    Dim beforeStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation - 0.01)
                    Dim afterStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation + 0.01)
                    'сравниваем текущую высоту по блокам и высоту луча(общую высоту стенки)
                    If beforeStep < afterStep Then
                        _blocksCount += 1
                    ElseIf beforeStep > afterStep Then
                        _blocksCount -= 1
                    Else
                        Throw New Exception("что-то неладное")
                    End If

                End If

                'создание выравнивающего слоя
                Dim rows As Integer = _blocksCount
                paramsLong.Item("BlocksCount").Value = rows
                rows = paramsLong.Item("BlocksCount").Value
                Dim levelWidth = 0.21
                'создание облицовочных блоков
                createFacingTW(corridorState, dWallHeight, flip, rows, dH, levelWidth, newOrigin)
            'End If

        Else 'for layout mode
            '----------------------------------
            'строим шаблон конструкции
            '----------------------------------
            Dim levelWidth = 0.21
            Dim levelH = 0.1
            Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH

            'создание облицовочных блоков
            createFacingTW(corridorState, dWallHeight, flip, blockLayers, dH, levelWidth, oOrigin)
        End If
        ' Обновляем входные параметры (если требуется)
        paramsLong.Add(Utilities.Side, side)
        paramsString.Add("AssemblyName", dAssemblyNameDefault)
        paramsLong.Add("BlocksInLayout", blockLayers)
        paramsDouble.Add("BlocksDeltaH", dH)
        paramsDouble.Add("BlockLength", dL)
    End Sub
    'создание конструкции
    Public Sub createFacingTW(ByVal corridorState As CorridorState, ByVal dWallHeight As Double, ByVal flipValue As Double, ByVal blockRows As Integer, ByVal delHeight As Double, ByVal levelingWidth As Double, ByVal origin As PointInMem)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        'Dim levelWidth = 0.21
        'Dim levelH = 0.1
        'Dim dWallHeight = blockRows * (dBlockHeight + delHeight) + levelH

        'создание облицовочных блоков
        Dim totalOffset = blockRows * dBlockOffset
        Dim totalHeight = blockRows * (dBlockHeight + delHeight)

        'точки вставки облицовочных блоков
        Dim newAddPoint As New PointInMem With {
        .Offset = origin.Offset - totalOffset * flipValue,
        .Elevation = origin.Elevation
        }
        'точка вставки омоноличивания
        Dim i As Integer = 1
        createLowerBlock(corridorState, newAddPoint, i, flipValue)
        i += 1
        Dim levelingTopPoint As New PointInMem With {
        .Elevation = origin.Elevation + dWallHeight,
        .Offset = 0
        }
        Dim topPntAdd As New PointInMem With {
             .Offset = origin.Offset,
            .Elevation = origin.Elevation + blockRows * (dBlockHeight + delHeight)
            }
        levelingTop(corridorState, levelingTopPoint, topPntAdd, levelingWidth, flipValue)

        While i <= blockRows
            newAddPoint.Offset += dBlockOffset * flipValue
            newAddPoint.Elevation += (dBlockHeight + delHeight)
            createBlock(corridorState, newAddPoint, i, flipValue)
            i += 1
        End While
        newAddPoint.Offset += dBlockOffset * flipValue
        newAddPoint.Elevation += (dBlockHeight + delHeight)

        Dim oParam As IParam
        oParam = paramsLong.Add("BlocksCount", blockRows)
        If oParam IsNot Nothing Then
            oParam.Access = ParamAccessType.Output
        End If
        oParam = paramsDouble.Add("BlockHeight", dBlockHeight)
        If oParam IsNot Nothing Then
            oParam.Access = ParamAccessType.Output
        End If
    End Sub
    'создание облицовочного блока
    Public Sub createBlock(corridorState As CorridorState, addPoint As PointInMem, rowNum As Integer, flip As Double)
        '--------------
        Dim blockPoints As PointCollection
        blockPoints = corridorState.Points

        Dim blockLinks As LinkCollection
        blockLinks = corridorState.Links

        Dim blockShapes As ShapeCollection
        blockShapes = corridorState.Shapes

        Dim P1 As Point
        Dim P2 As Point
        Dim P3 As Point
        Dim P4 As Point
        Dim P5 As Point
        Dim P6 As Point
        Dim P7 As Point
        Dim P8 As Point
        Dim P9 As Point
        Dim P10 As Point
        Dim P11 As Point
        Dim P12 As Point
        Dim P13 As Point
        Dim P14 As Point
        Dim P15 As Point
        Dim P16 As Point

        'Dim P5 As Point

        Dim L1 As Link
        Dim L2 As Link
        Dim L3 As Link
        Dim L4 As Link
        Dim L5 As Link
        Dim L6 As Link
        Dim L7 As Link
        Dim L8 As Link
        Dim L9 As Link
        Dim L10 As Link
        Dim L11 As Link
        Dim L12 As Link
        Dim L13 As Link
        Dim L14 As Link
        Dim L15 As Link
        Dim L16 As Link

        Dim Shape As Autodesk.Civil.DatabaseServices.Shape
        '-------------------------------------------------
        Dim oFillet As Double = 0.01

        P1 = blockPoints.Add(addPoint.Offset + oFillet * flip, addPoint.Elevation, "")
        P2 = blockPoints.Add(P1.Offset - oFillet * flip, P1.Elevation + oFillet, "")
        P3 = blockPoints.Add(P2.Offset, P2.Elevation + dBlockHeight - 2 * oFillet, "")
        P4 = blockPoints.Add(P3.Offset + oFillet * flip, P3.Elevation + oFillet, "")
        P5 = blockPoints.Add(P3.Offset + 0.045 * flip, P4.Elevation, "")
        P6 = blockPoints.Add(P5.Offset + 0.009 * flip, P5.Elevation - 0.025, "")
        P7 = blockPoints.Add(P5.Offset + 0.105 * flip, P6.Elevation, "")
        P8 = blockPoints.Add(P7.Offset, P5.Elevation, "")
        P9 = blockPoints.Add(P8.Offset + 0.054 * flip, P8.Elevation, "")
        P10 = blockPoints.Add(P9.Offset + oFillet * flip, P9.Elevation - oFillet, "")
        P11 = blockPoints.Add(P10.Offset, P10.Elevation - dBlockHeight + 2 * oFillet, "")
        P12 = blockPoints.Add(P11.Offset - oFillet * flip, P11.Elevation - oFillet, "")
        P13 = blockPoints.Add(P12.Offset - 0.111 * flip, P12.Elevation, "")
        P14 = blockPoints.Add(P13.Offset - 0.009 * flip, P13.Elevation - 0.024, "")
        P15 = blockPoints.Add(P14.Offset - 0.041 * flip, P14.Elevation, "")

        P16 = blockPoints.Add(P15.Offset - 0.009 * flip, P1.Elevation, "ось_раскладки_блоков")

        L1 = blockLinks.Add(P1, P2, "лицевая_грань_облицовки")
        L2 = blockLinks.Add(P2, P3, "лицевая_грань_облицовки")
        L3 = blockLinks.Add(P3, P4, "лицевая_грань_облицовки")
        L4 = blockLinks.Add(P4, P5, "")
        L5 = blockLinks.Add(P5, P6, "")
        L6 = blockLinks.Add(P6, P7, "")
        L7 = blockLinks.Add(P7, P8, "")
        L8 = blockLinks.Add(P8, P9, "")
        L9 = blockLinks.Add(P9, P10, "")
        L10 = blockLinks.Add(P10, P11, "")
        L11 = blockLinks.Add(P11, P12, "")
        L12 = blockLinks.Add(P12, P13, "")
        L13 = blockLinks.Add(P13, P14, "")
        L14 = blockLinks.Add(P14, P15, "")
        L15 = blockLinks.Add(P15, P16, "")
        L16 = blockLinks.Add(P16, P1, "")

        Dim blockName As String = "TW1" 'CType(rowNum, String) + "_" + "TW1"

        Dim shapeLinks() = {L1, L2, L3, L4, L5, L6, L7, L8, L9, L10, L11, L12, L13, L14, L15, L16}
        Shape = blockShapes.Add(shapeLinks, blockName)

    End Sub
    'создание нижнего блока
    Public Sub createLowerBlock(corridorState As CorridorState, addPoint As PointInMem, rowNum As Integer, flip As Double)
        '--------------
        Dim blockPoints As PointCollection
        blockPoints = corridorState.Points

        Dim blockLinks As LinkCollection
        blockLinks = corridorState.Links

        Dim blockShapes As ShapeCollection
        blockShapes = corridorState.Shapes

        Dim P1 As Point
        Dim P2 As Point
        Dim P3 As Point
        Dim P4 As Point
        Dim P5 As Point
        Dim P6 As Point
        Dim P7 As Point
        Dim P8 As Point
        Dim P9 As Point
        Dim P10 As Point
        Dim P11 As Point
        Dim P12 As Point
        Dim P13 As Point

        Dim L1 As Link
        Dim L2 As Link
        Dim L3 As Link
        Dim L4 As Link
        Dim L5 As Link
        Dim L6 As Link
        Dim L7 As Link
        Dim L8 As Link
        Dim L9 As Link
        Dim L10 As Link
        Dim L11 As Link
        Dim L12 As Link

        Dim Shape As Autodesk.Civil.DatabaseServices.Shape
        '-------------------------------------------------
        Dim oFillet As Double = 0.01

        P1 = blockPoints.Add(addPoint.Offset + oFillet * flip, addPoint.Elevation, "")
        P2 = blockPoints.Add(P1.Offset - oFillet * flip, P1.Elevation + oFillet, "")
        P3 = blockPoints.Add(P2.Offset, P2.Elevation + dBlockHeight - 2 * oFillet, "")
        P4 = blockPoints.Add(P3.Offset + oFillet * flip, P3.Elevation + oFillet, "")
        P5 = blockPoints.Add(P3.Offset + 0.045 * flip, P4.Elevation, "")
        P6 = blockPoints.Add(P5.Offset + 0.009 * flip, P5.Elevation - 0.025, "")
        P7 = blockPoints.Add(P5.Offset + 0.105 * flip, P6.Elevation, "")
        P8 = blockPoints.Add(P7.Offset, P5.Elevation, "")
        P9 = blockPoints.Add(P8.Offset + 0.054 * flip, P8.Elevation, "")
        P10 = blockPoints.Add(P9.Offset + oFillet * flip, P9.Elevation - oFillet, "")
        P11 = blockPoints.Add(P10.Offset, P10.Elevation - dBlockHeight + 2 * oFillet, "")
        P12 = blockPoints.Add(P11.Offset - oFillet * flip, P11.Elevation - oFillet, "")

        P13 = blockPoints.Add(addPoint.Offset + (dBlockWidth) * flip / 2, addPoint.Elevation, "ось_раскладки_блоков")

        L1 = blockLinks.Add(P1, P2, "лицевая_грань_облицовки")
        L2 = blockLinks.Add(P2, P3, "лицевая_грань_облицовки")
        L3 = blockLinks.Add(P3, P4, "лицевая_грань_облицовки")
        L4 = blockLinks.Add(P4, P5, "")
        L5 = blockLinks.Add(P5, P6, "")
        L6 = blockLinks.Add(P6, P7, "")
        L7 = blockLinks.Add(P7, P8, "")
        L8 = blockLinks.Add(P8, P9, "")
        L9 = blockLinks.Add(P9, P10, "")
        L10 = blockLinks.Add(P10, P11, "")
        L11 = blockLinks.Add(P11, P12, "")
        L12 = blockLinks.Add(P12, P1, "")

        Dim blockName As String = "TW1" 'CType(rowNum, String) + "_" + "TW1"

        Dim shapeLinks() = {L1, L2, L3, L4, L5, L6, L7, L8, L9, L10, L11, L12}
        Shape = blockShapes.Add(shapeLinks, blockName)

    End Sub
    'доболнительные сечения в точках скачка выравнивающего слоя
    Public Sub createAddStations(tm As DBTransactionManager, corridorState As CorridorState, blockStep As Double, blockLength As Double, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        Dim sliseStep = blockLength / 2
        Do While stationCurr < endSt
            Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
            Dim remainder = wallHeight Mod blockStep
            If Math.Abs(remainder) < 0.001 Then
                Dim rem1 = stationCurr Mod sliseStep
                Dim backSlice = stationCurr - rem1
                Dim rem2 = sliseStep - rem1
                Dim frontSlice = stationCurr + rem2
                Dim backH = target.GetElevation(alignmentId, backSlice)
                Dim frontH = target.GetElevation(alignmentId, frontSlice)
                If frontH <= backH Then
                    sectionsToAdd.Add(backSlice)
                    sectionsToAddStep.Add(backSlice + 0.001)
                Else
                    sectionsToAdd.Add(frontSlice)
                    sectionsToAddStep.Add(frontSlice + 0.001)
                End If
                stationCurr += sliseStep
            End If
            stationCurr += stateStep
        Loop

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
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description1 = "доп.сечения облицовочных блоков " + baseline.Name
                            If info.Description = description1 Then
                                reg.DeleteStation(info.Station)
                            End If
                            Dim description2 = "скачок облицовки " + baseline.Name
                            If info.Description = description2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff1 = sectionsToAdd.Except(assemblyStations)
                        Dim diff2 = sectionsToAddStep.Except(assemblyStations)
                        For Each station In diff1
                            Try
                                reg.AddStation(station, "доп.сечения облицовочных блоков " + baseline.Name)
                            Catch

                            End Try
                        Next
                        For Each station In diff2
                            Try
                                reg.AddStation(station, "скачок облицовки " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
    'условие для пересчета высоты облицовки (создание ступени)
    Public Function isStep(tm As DBTransactionManager, corridorState As CorridorState, stationCurr As Double)
        Dim result As Boolean = False
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
                        'получаем свойства доп сечений
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "скачок облицовки " + baseline.Name
                            If info.Description = description And stationCurr = info.Station Then
                                result = True
                            End If
                        Next
                    End If
                Next
            End If
        Next
        Return result
    End Function
    'метод для создания выравнивающей ленты
    Public Sub levelingTop(ByVal corridorState As CorridorState,
                           ByVal topPoint As PointInMem,
                           ByVal lowPoint As PointInMem,
                           ByVal Width As Double,
                           ByVal flip As Double)
        Dim levelPoints As PointCollection
        levelPoints = corridorState.Points

        Dim levelLinks As LinkCollection
        levelLinks = corridorState.Links

        Dim levelShapes As ShapeCollection
        levelShapes = corridorState.Shapes

        Dim oLevelP1 As Point
        Dim oLevelP2 As Point
        Dim oLevelP3 As Point
        Dim oLevelP4 As Point

        Dim oLevelL1 As Link
        Dim oLevelL2 As Link
        Dim oLevelL3 As Link
        Dim oLevelL4 As Link

        Dim oLevelShape As Autodesk.Civil.DatabaseServices.Shape
        If topPoint.Elevation < lowPoint.Elevation Then
            topPoint.Elevation = lowPoint.Elevation
        End If
        oLevelP1 = levelPoints.Add(lowPoint.Offset, lowPoint.Elevation, "Низ выравнивающего слоя")
        oLevelP2 = levelPoints.Add(topPoint.Offset, topPoint.Elevation, "Верх выравнивающего слоя")
        oLevelP3 = levelPoints.Add(oLevelP2.Offset + Width * flip, oLevelP2.Elevation, "")
        oLevelP4 = levelPoints.Add(oLevelP1.Offset + Width * flip, oLevelP1.Elevation, "")

        oLevelL1 = levelLinks.Add(oLevelP1, oLevelP2, "")
        oLevelL2 = levelLinks.Add(oLevelP2, oLevelP3, "")
        oLevelL3 = levelLinks.Add(oLevelP3, oLevelP4, "")
        oLevelL4 = levelLinks.Add(oLevelP4, oLevelP1, "")

        oLevelShape = levelShapes.Add(oLevelL1, oLevelL2, oLevelL3, oLevelL4, "Выравнивающий слой под МШБ")
    End Sub
    Public Sub createAddStationsForProfile(tm As DBTransactionManager, corridorState As CorridorState, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        'Dim startSt = corridorState.CurrentRegionStartStation
        'Dim stateStep As Double = 0.001
        'Dim endSt = corridorState.CurrentRegionEndStation
        'Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        'Dim sliseStep = blockLength / 2
        'Do While stationCurr < endSt
        '    Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
        '    Dim remainder = wallHeight Mod blockStep
        '    If Math.Abs(remainder) < 0.001 Then
        '        Dim rem1 = stationCurr Mod sliseStep
        '        Dim backSlice = stationCurr - rem1
        '        Dim rem2 = sliseStep - rem1
        '        Dim frontSlice = stationCurr + rem2
        '        Dim backH = target.GetElevation(alignmentId, backSlice)
        '        Dim frontH = target.GetElevation(alignmentId, frontSlice)
        '        If frontH <= backH Then
        '            sectionsToAdd.Add(backSlice)
        '            sectionsToAddStep.Add(backSlice + 0.001)
        '        Else
        '            sectionsToAdd.Add(frontSlice)
        '            sectionsToAddStep.Add(frontSlice + 0.001)
        '        End If
        '        stationCurr += sliseStep
        '    End If
        '    stationCurr += stateStep
        'Loop

        Dim profileH As Autodesk.Civil.DatabaseServices.Profile = tm.GetObject(target.TargetId, OpenMode.ForRead)

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
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description1 = "доп.сечения облицовочных блоков " + baseline.Name
                            If info.Description = description1 Then
                                reg.DeleteStation(info.Station)
                            End If
                            Dim description2 = "скачок облицовки " + baseline.Name
                            If info.Description = description2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next

                        Dim pEnts = profileH.Entities
                        For Each ent In pEnts
                            If ent.StartElevation <> ent.EndElevation And ent.StartStation > reg.StartStation And ent.StartStation < reg.EndStation Then
                                sectionsToAdd.Add(ent.StartStation)
                                sectionsToAddStep.Add(ent.EndStation)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff1 = sectionsToAdd.Except(assemblyStations)
                        Dim diff2 = sectionsToAddStep.Except(assemblyStations)
                        For Each station In diff1
                            Try
                                reg.AddStation(station, "доп.сечения облицовочных блоков " + baseline.Name)
                            Catch

                            End Try
                        Next
                        For Each station In diff2
                            Try
                                reg.AddStation(station, "скачок облицовки " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
End Class
