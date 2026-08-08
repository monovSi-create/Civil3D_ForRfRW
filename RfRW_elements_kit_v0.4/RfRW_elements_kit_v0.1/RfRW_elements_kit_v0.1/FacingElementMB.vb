
Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.Internal
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil.Runtime
Imports Autodesk.AutoCAD.DatabaseServices
Imports System.Math
Imports System.Security.Policy
Imports Autodesk.Civil.ApplicationServices
Imports System.Security.Cryptography
Public Class FacingElementMB
    Inherits SATemplate

    Private Const baseL1 = 0.165
    Private Const baseL2 = 0.135
    Private Const toothH1 = 0.05
    Private Const toothL1 = 0.02
    Private Const cutL1 = 0.03
    Private Const faceHeight = 0.44
    Private Const dBlocksInLayout = 5
    Private Const deltaH = 0.000
    Private Const dBlockHeight = 0.5
    Private Const dBlockLength = 1.405
    Private Const dBlockOffset = 0.0
    Private Const SideDefault = Utilities.Right
    Private Const dAssemblyNameDefault = "Участок"
    Private Shared _blocksCount As Integer = 0

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
        oParam = paramsString.Add("Имя участка", dAssemblyNameDefault)
        oParam = paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        If oParam IsNot Nothing Then oParam.DisplayName = "Блоков в шаблоне конструкции"
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
        Dim paramsDouble As ParamDoubleCollection = corridorState.ParamsDouble
        Dim paramsLong As ParamLongCollection = corridorState.ParamsLong
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
        'Dim oIntersectionPointWithSurface As IPoint = Nothing

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble
        '----------------------------------------
#Region "Присваивание переменным значений входных параметров"
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
        '----------------------------------------
        Dim dH As Double
        Try
            dH = paramsDouble.Value("BlocksDeltaH")
        Catch
            dH = deltaH
        End Try
        '----------------------------------------
        Dim dL As Double
        Try
            dL = paramsDouble.Value("BlockLength")
        Catch
            dL = dBlockLength
        End Try
        '----------------------------------------
        Dim oRegName As String
        Try
            oRegName = paramsString.Value("Имя участка")
        Catch
            oRegName = dAssemblyNameDefault
        End Try
#End Region
        'create block collection
        Dim blockPoints As PointCollection
        blockPoints = corridorState.Points
        Dim blockLinks As LinkCollection
        blockLinks = corridorState.Links
        Dim blockShapes As ShapeCollection
        blockShapes = corridorState.Shapes
        'create topLeveling collection
        Dim levelPoints As PointCollection
        levelPoints = corridorState.Points
        Dim levelLinks As LinkCollection
        levelLinks = corridorState.Links
        Dim levelShapes As ShapeCollection
        levelShapes = corridorState.Shapes

        Dim centralPoint As Point = blockPoints.Add((cutL1 + baseL2 + toothL1 + baseL1) / 2 * flip, 0, "center")
        Dim backPoint As Point = blockPoints.Add((cutL1 + baseL2 + toothL1 + baseL1) * flip, -1 * toothH1, "back")

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)
        Dim blockStep = dBlockHeight + dH

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
                'get elevation on elevationTarget
                Try
                    dWallHeight = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallHeightProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If
            'определяем профиль облицовочных блоков (если имеется)
            Dim blocksElevTarget As SlopeElevationTarget
            Try
                blocksElevTarget = oParamsElevationTarget.Value("blocksTop")
            Catch
                blocksElevTarget = Nothing
            End Try

            Dim hasWallBlocksProfile As Boolean
            hasWallBlocksProfile = False
            Dim blocksHeight As Double

            If Not blocksElevTarget Is Nothing Then
                'получим высоту по профилю
                Try
                    blocksHeight = blocksElevTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallBlocksProfile = True

                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "blocksTop", "RetainWallVertical")
                End Try
            End If

            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / blockStep, Integer)
                createAddStationsForProfile(tm, corridorState, blocksElevTarget)
            Else
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
                    'вспомогательные ветрора до и после скачка для оценки направления проектного профиля
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
                rows = _blocksCount
            End If

            Dim levelWidth = 0.35 'ширина выравнивающего слоя по облицовке
            '--------------------------------
            'построение конструкции в сечении
            '--------------------------------
            Dim pointToSoil As New PointInMem
            Dim pointToSubbase As New PointInMem 'точка для вставки щебеночной подготовки
            Dim zeroPoint As New PointInMem With {
                .Offset = 0,
                .Elevation = 0
                }
            'создание облицовочных блоков
            createCladdingMB(corridorState, dWallHeight, flip, rows, blockStep, dH, levelWidth, zeroPoint, pointToSoil)

        Else 'for layout mode
            '----------------------------------
            'строим шаблон конструкции
            '----------------------------------
            'создание облицовочных блоков
            Dim levelWidth = 0.35
            Dim levelH = 0.1
            'Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH
            Dim dWallHeightElevation As Double = 5
            Dim blockRows As Integer = dWallHeightElevation * 1000 \ blockStep * 1000

            Dim pointToSoil As New PointInMem  'точка для вставки армогрунта
            'создание облицовочных блоков
            createCladdingMB(corridorState, dWallHeightElevation, flip, blockRows, blockStep, dH, levelWidth, oOrigin, pointToSoil)

        End If
        ' Обновляем входные параметры (если требуется)
        paramsLong.Add(Utilities.Side, side)
        paramsString.Add("Имя участка", oRegName)
        paramsLong.Add("BlocksInLayout", blockLayers)
        paramsDouble.Add("BlocksDeltaH", dH)
        paramsDouble.Add("BlockLength", dL)
    End Sub
    'создание конструкции
    Public Sub createCladdingMB(ByVal corridorState As CorridorState, ByVal dWallHeight As Double, ByVal flipValue As Double, ByVal blockRows As Integer, ByVal blockVerticalStep As Double, ByVal delHeight As Double, ByVal levelingWidth As Double, ByVal origin As PointInMem, ByRef outputPoint As PointInMem)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        'создание облицовочных блоков

        'общая высота по облицовочным блокам
        'Dim totalHeight = blockRows * blockVerticalStep
        'точки вставки облицовочных блоков
        Dim newAddPoint As New PointInMem With {
        .Offset = 0,
        .Elevation = origin.Elevation
        }
        'точка вставки омоноличивания
        Dim levelingTopPoint As New PointInMem With {
            .Offset = cutL1 * flipValue,
            .Elevation = origin.Elevation + dWallHeight
        }
        Dim topPntAdd As New PointInMem With {
            .Offset = cutL1 * flipValue,
            .Elevation = origin.Elevation + blockRows * (dBlockHeight + delHeight)
            }
        levelingTop(corridorState, levelingTopPoint, topPntAdd, flipValue)
        'точка вывода середины первого блока
        outputPoint.Offset = newAddPoint.Offset + flipValue * (baseL2 + cutL1)
        outputPoint.Elevation = newAddPoint.Elevation
        Dim i As Integer = 1
        While i <= blockRows
            createBlock(corridorState, newAddPoint, flipValue)
            newAddPoint.Offset += dBlockOffset * flipValue
            newAddPoint.Elevation += blockVerticalStep
            i += 1
        End While
        'newAddPoint.Offset += cutL1 * flipValue
        'newAddPoint.Elevation += blockVerticalStep

    End Sub
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
    'создание облицовочного блока
    Public Sub createBlock(ByVal corridorState As CorridorState, addPoint As PointInMem, flip As Double)
        Dim blockPoints As PointCollection = corridorState.Points
        Dim blockLinks As LinkCollection = corridorState.Links
        Dim blockShapes As ShapeCollection = corridorState.Shapes

        Dim blockName As String = "Облицовочный блок МБ1"
        Dim blockF As String = "Лицевая грань облицовочного блока"
        Dim blockT As String = "Верхняя грань облицовочного блока"

        Dim blockPoint1 As Point
        Dim blockPoint2 As Point
        Dim blockPoint3 As Point
        Dim blockPoint4 As Point
        Dim blockPoint5 As Point
        Dim blockPoint6 As Point
        Dim blockPoint7 As Point
        Dim blockPoint8 As Point
        Dim blockPoint9 As Point
        Dim blockPoint10 As Point

        Dim blockLink1 As Link
        Dim blockLink2 As Link
        Dim blockLink3 As Link
        Dim blockLink4 As Link
        Dim blockLink5 As Link
        Dim blockLink6 As Link
        Dim blockLink7 As Link
        Dim blockLink8 As Link
        Dim blockLink9 As Link
        Dim blockLink10 As Link

        Dim blockShape1 As Autodesk.Civil.DatabaseServices.Shape

        blockPoint1 = blockPoints.Add(addPoint.Offset + (cutL1 + baseL2) * flip, addPoint.Elevation, "Точка раскладки облицовочных блоков")
        blockPoint2 = blockPoints.Add(blockPoint1.Offset - baseL2 * flip, blockPoint1.Elevation, "")
        blockPoint3 = blockPoints.Add(blockPoint2.Offset - cutL1 * flip, blockPoint2.Elevation + cutL1, "")
        blockPoint4 = blockPoints.Add(blockPoint3.Offset, blockPoint3.Elevation + faceHeight, "")
        blockPoint5 = blockPoints.Add(blockPoint4.Offset + cutL1 * flip, blockPoint4.Elevation + cutL1, "")
        blockPoint6 = blockPoints.Add(blockPoint5.Offset + baseL2 * flip, blockPoint5.Elevation, "")
        blockPoint7 = blockPoints.Add(blockPoint6.Offset + toothL1 * flip, blockPoint6.Elevation - toothH1, "")
        blockPoint8 = blockPoints.Add(blockPoint7.Offset + baseL1 * flip, blockPoint7.Elevation, "")
        blockPoint9 = blockPoints.Add(blockPoint8.Offset, blockPoint8.Elevation - dBlockHeight, "")
        blockPoint10 = blockPoints.Add(blockPoint9.Offset - baseL1 * flip, blockPoint9.Elevation, "")

        blockLink1 = blockLinks.Add(blockPoint1, blockPoint2, "")
        blockLink2 = blockLinks.Add(blockPoint2, blockPoint3, "")
        blockLink3 = blockLinks.Add(blockPoint3, blockPoint4, "")
        blockLink4 = blockLinks.Add(blockPoint4, blockPoint5, "")
        blockLink5 = blockLinks.Add(blockPoint5, blockPoint6, blockT)
        blockLink6 = blockLinks.Add(blockPoint6, blockPoint7, blockT)
        blockLink7 = blockLinks.Add(blockPoint7, blockPoint8, blockT)
        blockLink8 = blockLinks.Add(blockPoint8, blockPoint9, "")
        blockLink9 = blockLinks.Add(blockPoint9, blockPoint10, "")
        blockLink10 = blockLinks.Add(blockPoint10, blockPoint1, "")

        Dim linkCollect As Link() = {blockLink1, blockLink2, blockLink3, blockLink4, blockLink5, blockLink6, blockLink7, blockLink8, blockLink9, blockLink10}

        blockShape1 = blockShapes.Add(linkCollect, blockName)
        '----------------------------------------------
        'создание звена для подсчета площади облицовки
        Dim fP1 As Point = blockPoints.Add(addPoint.Offset, addPoint.Elevation, "")
        Dim fP2 As Point = blockPoints.Add(addPoint.Offset + 0.001 * flip, addPoint.Elevation + (cutL1 * 2 + faceHeight), "")
        Dim fL1 As Link = blockLinks.Add(fP1, fP2, blockF)
    End Sub

    'метод для создания выравнивающей ленты
    Public Sub levelingTop(ByVal corridorState As CorridorState,
                           ByVal topPoint As PointInMem,
                           ByVal lowPoint As PointInMem,
                           ByVal flip As Double)

        Dim levelPoints As PointCollection = corridorState.Points
        Dim levelLinks As LinkCollection = corridorState.Links
        Dim levelShapes As ShapeCollection = corridorState.Shapes

        Dim levTopName As String = "Выравнивающая лента"

        If topPoint.Elevation < lowPoint.Elevation Then
            topPoint.Elevation = lowPoint.Elevation
        End If

        Dim levPoint1 = levelPoints.Add(lowPoint.Offset, lowPoint.Elevation, "Низ выравнивающего слоя")
        Dim levPoint2 = levelPoints.Add(topPoint.Offset, topPoint.Elevation, "Верх выравнивающего слоя")
        Dim levPoint3 = levelPoints.Add(levPoint2.Offset + (baseL1 + baseL2 + toothL1) * flip, levPoint2.Elevation, "")
        Dim levPoint4 = levelPoints.Add(levPoint1.Offset + baseL2 * flip, levPoint1.Elevation, "")
        Dim levPoint5 = levelPoints.Add(levPoint4.Offset + toothL1 * flip, levPoint4.Elevation - toothH1, "")
        Dim levPoint6 = levelPoints.Add(levPoint5.Offset + baseL1 * flip, levPoint5.Elevation, "")

        Dim levLink1 = levelLinks.Add(levPoint1, levPoint2, levTopName)
        Dim levLink2 = levelLinks.Add(levPoint2, levPoint3, "")
        Dim levLink3 = levelLinks.Add(levPoint1, levPoint4, "")
        Dim levLink4 = levelLinks.Add(levPoint4, levPoint5, "")
        Dim levLink5 = levelLinks.Add(levPoint5, levPoint6, "")
        Dim levLink6 = levelLinks.Add(levPoint3, levPoint6, "")

        Dim levLinks As Link() = {levLink1, levLink2, levLink6, levLink5, levLink4, levLink3}

        Dim levShape = levelShapes.Add(levLinks, levTopName)
    End Sub
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
