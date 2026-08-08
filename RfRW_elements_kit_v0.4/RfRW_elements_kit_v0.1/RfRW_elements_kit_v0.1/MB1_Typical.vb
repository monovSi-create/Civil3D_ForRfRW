Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Imports Shape = Autodesk.Civil.DatabaseServices.Shape
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode
Imports Microsoft.VisualBasic.Devices
Imports System.Net

Public Class MB1_Typical
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: MB1_Wall
    '
    '   Description: Creates a simple cross-sectional representation of a reinforcement soil wall composed of an array of geogrids.Attachment origin
    '                is at bottom.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetSurface              Surface    Yes       May be used to judge fill/cut condition
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                Side                   long        no          Right            specifies side to place SA on
    '                Width                  double      no          3.0              width of geogrids
    '                Step                   double      no          0.5              step of geogrid layer
    '                GravelSlope            double      no          1.5              0
    '                DrenageOffset          double      no          0.3              0
    '                DrenageElevation       long        no           1               0
    '                GeotextileOverlap      double      no          0.3              0
    '                BaseElevation          double      no          0.15             0
    '                RE520_count            long        no          0                0
    '                RE540_count            long        no          0                0
    '                RE560_count            long        no          0                0
    '                RE570_count            long        no          0                0
    '                RE580_count            long        no          0                0
    '                RE520_length           double      no          3                0
    '                RE540_length           double      no          3                0
    '                RE560_length           double      no          3                0
    '                RE570_length           double      no          3                0
    '                RE580_length           double      no          3                0
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const dLayerStepDefault = 0.5
    Private Const dGravelSlopeDefault = 1.5
    Private Const dDrenageOffsetDefault = 0.3
    Private Const dDrenageElevationDefault = 1
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dBaseElevationDefault = 0.25
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    Private Const dSubAsNameDefault = "Участок"
    'Private Const blocksAboveGrid As Integer = 0
    'Private Const dBlocksCount As Integer = 1
    'Private Const dBlockH = 0.5
    Private Const dBlocksInLayout = 5
    Private Const dBlockHeight = 0.5
    Private Const deltaH = 0.000
    Private Const dBlockOffset = 0.0
    Private Const dBlockLength = 1.405
    Private Const WidthDefaultF = 0.8
    Private Const HeightDefaultF = 0.3
    Private Const dPrepHeight = 0.02
    Private Const dPipeSlope = 0.05
    Private Const dPipeStep = 5.0
    Private Const dPipeDiametr = 0.16
    Private Const baseL1 = 0.165
    Private Const baseL2 = 0.135
    Private Const toothH1 = 0.05
    Private Const toothL1 = 0.02
    Private Const cutL1 = 0.03
    Private Const faceHeight = 0.44
    Private Const dDualReinf = False
    Private Const dDualReinfCount = 0
    Private Const dDualReinfAbove = False

    Private Shared _blocksCount As Integer = 0 'необходима для хранения значения на протяжении перестроения всего коридора
    'Private Const dRE520_lengthDefault = 3
    'Private Const dRE540_lengthDefault = 3
    'Private Const dRE560_lengthDefault = 3
    'Private Const dRE570_lengthDefault = 3
    'Private Const dRE580_lengthDefault = 3


    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("DesignProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"

        ParamLong = paramsLong.Add("blocksTop", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных блоков"

        ParamLong = paramsLong.Add("BackProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль тыльной стороны"
        ParamLong = paramsLong.Add("FrontProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль лицевой стороны"

    End Sub
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

        Dim paramsBool As ParamBoolCollection = corridorState.ParamsBool
        ' Add input parameters we used in this script

        'paramsDouble.Add("Наклон лицевой грани", dFaceAngleDefault)
        'paramsDouble.Add("Горизонтальное смещение", dHorizOffset)
        paramsDouble.Add("Длина георешеток", dGridWidthDefault)
        paramsDouble.Add("Шаг георешеток", dLayerStepDefault)
        paramsDouble.Add("Наклон дренажных призм", dGravelSlopeDefault)
        paramsDouble.Add("Ширина дренажных призм", dDrenageOffsetDefault)
        paramsDouble.Add("Отступ первого слоя георешетки", dBaseElevationDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsLong.Add("Отступ первого слоя дренажа", dDrenageElevationDefault)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        'paramsLong.Add("bCount", dBlocksCount)
        'paramsDouble.Add("bHeight", dBlockH)
        'paramsLong.Add("BlocksAboveGrid", blocksAboveGrid)
        'paramsDouble.Add("RE580_length", dRE580_lengthDefault)
        'paramsDouble.Add("RE570_length", dRE570_lengthDefault)
        'paramsDouble.Add("RE560_length", dRE560_lengthDefault)
        'paramsDouble.Add("RE540_length", dRE540_lengthDefault)
        'paramsDouble.Add("RE520_length", dRE520_lengthDefault)
        'paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        paramsDouble.Add("BlocksDeltaH", deltaH)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
        paramsBool.Add("Геотекстиль в основании", True)
        paramsBool.Add("Двойное геоармирование", dDualReinf)
        paramsLong.Add("Кол-во нижних рядов блоков двойного армирования", dDualReinfCount)
        paramsLong.Add("Кол-во верхних рядов блоков двойного армирования", dDualReinfCount)
        paramsBool.Add("Выпуск герешетки из середины блока (выше двойного геоармирования)", dDualReinfAbove)
    End Sub

    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

    End Sub

    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        Dim paramsBool As ParamBoolCollection = corridorState.ParamsBool
#Region "Присваивание переменным значений параметров"

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
        'geogrid dimensions
        Dim oGridWidth As Double
        Try
            oGridWidth = paramsDouble.Value("Длина георешеток")
        Catch
            oGridWidth = dGridWidthDefault
        End Try
        '----------------------------------------
        Dim hasGtxtLower As Boolean
        Try
            hasGtxtLower = paramsBool.Value("Геотекстиль в основании")
        Catch
            hasGtxtLower = True
        End Try
        '----------------------------------------
        'Dim oFaceAngle As Double
        '    Try
        '        oFaceAngle = paramsDouble.Value("Наклон лицевой грани")
        '    Catch
        '        oFaceAngle = dFaceAngleDefault
        '    End Try
        '----------------------------------------
        Dim gStep As Double
        Try
            gStep = paramsDouble.Value("Шаг георешеток")
        Catch
            gStep = dLayerStepDefault
        End Try
        '----------------------------------------
        Dim oDrenageSlope As Double
        Try
            oDrenageSlope = paramsDouble.Value("Заложение дренажных призм")
        Catch
            oDrenageSlope = dGravelSlopeDefault
        End Try
        '----------------------------------------
        Dim oDrenageOffset As Double
        Try
            oDrenageOffset = paramsDouble.Value("Ширина дренажных призм")
        Catch
            oDrenageOffset = dDrenageOffsetDefault
        End Try
        '----------------------------------------
        Dim oDrenageElevation As Long
        Try
            oDrenageElevation = paramsLong.Value("Отступ первого слоя дренажа")
        Catch
            oDrenageElevation = dDrenageElevationDefault
        End Try
        '----------------------------------------
        Dim oBaseElevation As Double
        Try
            oBaseElevation = paramsDouble.Value("Отступ первого слоя георешетки")
        Catch
            oBaseElevation = dBaseElevationDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        Dim RE580_count As Long
        Try
            RE580_count = paramsLong.Value("Кол-во RE580")
        Catch
            RE580_count = dRE580_countDefault
        End Try
        '----------------------------------------
        Dim RE570_count As Long
        Try
            RE570_count = paramsLong.Value("Кол-во RE570")
        Catch
            RE570_count = dRE570_countDefault
        End Try
        '----------------------------------------
        Dim RE560_count As Long
        Try
            RE560_count = paramsLong.Value("Кол-во RE560")
        Catch
            RE560_count = dRE560_countDefault
        End Try
        '----------------------------------------
        Dim RE540_count As Long
        Try
            RE540_count = paramsLong.Value("Кол-во RE540")
        Catch
            RE540_count = dRE540_countDefault
        End Try
        '----------------------------------------
        Dim RE520_count As Long
        Try
            RE520_count = paramsLong.Value("Кол-во RE520")
        Catch
            RE520_count = dRE520_countDefault
        End Try
        '----------------------------------------
        Dim oSubAsName As String
        Try
            oSubAsName = paramsString.Value("Имя участка")
        Catch
            oSubAsName = dSubAsNameDefault
        End Try
        '----------------------------------------
        'Dim bAboveGrid As Integer
        'Try
        '    bAboveGrid = paramsLong.Value("BlocksAboveGrid")
        'Catch
        '    bAboveGrid = blocksAboveGrid
        'End Try
        '----------------------------------------
        'Dim blockHeight As Double
        'Try
        '    blockHeight = paramsDouble.Value("bHeight")
        'Catch
        '    blockHeight = dBlockH
        'End Try
        '----------------------------------------
        ' Dim blockCount As Long
        ' Try
        '     blockCount = paramsLong.Value("bCount")
        ' Catch
        '     blockCount = dBlocksCount
        ' End Try
        '----------------------------------------
        'Dim blockLayers As Long
        'Try
        '    blockLayers = paramsLong.Value("BlocksInLayout")
        'Catch
        '    blockLayers = dBlocksInLayout
        'End Try
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
        Dim prepHeight As Double
        Try
            prepHeight = paramsDouble.Value("Толщина Подготовки")
        Catch
            prepHeight = dPrepHeight
        End Try
        '----------------------------------------
        'foundation dimensions
        Dim widthF As Double
        Try
            widthF = paramsDouble.Value("Ширина Фундамента")
        Catch
            widthF = WidthDefaultF
        End Try
        '-----------------------------------------
        Dim heightF As Double
        Try
            heightF = paramsDouble.Value("Высота Фундамента")
        Catch
            heightF = HeightDefaultF
        End Try
        '----------------------------------------
        Dim oPipeStep As Double
        Try
            oPipeStep = paramsDouble.Value("Шаг дренажных выпусков") / 2
        Catch
            oPipeStep = dPipeStep / 2
        End Try
        '-----------------------
        Dim oPipeSlope As Double
        Try
            oPipeSlope = paramsDouble.Value("Уклон дренажной трубы")
        Catch
            oPipeSlope = dPipeSlope
        End Try
        '-----------------------
        Dim oPipeD As Double
        Try
            oPipeD = paramsDouble.Value("Диаметр дренажной трубы")
        Catch
            oPipeD = dPipeDiametr
        End Try
        '-----------------------
        Dim odualR As Boolean
        Dim odualRCountLow As Long
        Dim odualRCountUp As Long
        Dim odualRtoMiddle As Boolean
        Try
            odualR = paramsBool.Value("Двойное армирование")
            If odualR Then
                Try
                    odualRCountLow = paramsLong.Value("Кол-во нижних рядов блоков двойного армирования")
                Catch
                    odualRCountLow = dDualReinfCount
                End Try
            End If
            If odualR Then
                Try
                    odualRCountUp = paramsLong.Value("Кол-во верхних рядов блоков двойного армирования")
                Catch
                    odualRCountUp = dDualReinfCount
                End Try
            End If
            If odualR Then
                Try
                    odualRtoMiddle = paramsBool.Value("Выпуск герешетки из середины блока (выше двойного геоармирования)")
                Catch
                    odualRtoMiddle = dDualReinfAbove
                End Try
            End If
        Catch
            odualR = dDualReinf
        End Try
        '-----------------------------------------------
#End Region
        ' Check user input
        If oGridWidth < 3 Then
            Utilities.RecordError(corridorState, CorridorError.ValueTooSmall, "Длина георешеток", "Geogrid")
            oGridWidth = dGridWidthDefault
        End If

        If gStep <= 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanOrEqualToZero, "Шаг георешеток", "Geogrid")
            gStep = dLayerStepDefault
        End If

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        Dim dWallWidthOffset As Double = (oGridWidth + 1.0) * flip 'soil width
        Dim blockStep = dBlockHeight + dH

        If corridorState.Mode <> CorridorMode.Layout Then 'для сечений коридора
            If corridorState.Mode = CorridorMode.None Then
                Throw New Exception("NONE")
            End If
            '--------------------------------------------------------
            'анализируем наличие целей для сбора информации в сечении
            '--------------------------------------------------------

            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("DesignProf")
            Catch
                elevationTarget = Nothing
            End Try

            Dim hasWallHeightProfile As Boolean
            hasWallHeightProfile = False
            Dim dWallHeightElevation As Double

            If Not elevationTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallHeightElevation = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallHeightProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "DesignProf", "RetainWallVertical")
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
                'сечение по заданному профилю высоты блоков+проектному

            End If
            'Определяем глубину отступа для грунта засыпки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try

            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    dWallWidthOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            End If
            'Dim startStep As Boolean
            'Dim endStep As Boolean
            'Dim beforeNameRegion As String
            'Dim afterNameRegion As String
            'Dim tangSlope As Double = 1 / oStepSlope
            'Dim subHeight As Double = oGCount * oHeight



            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / blockStep, Integer)
                createAddStationsForProfile(tm, corridorState, blocksElevTarget)

            Else 'в случае отсутствия профиля для определения высоты облицовки (для первого прохода например)
                'в начале каждого региона(области) добавляем сечения в пикетах шага облицовочного блока МБ1
                If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, blockStep, dL, elevationTarget) 'доп сечения для облицовки
                    'SubbaseAddStations(tm, corridorState, oStepOffset, tangSlope, startStep, endStep, oFStep, oGCount, oHeight)
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = blockStep * 1000
                    _blocksCount = dWallHeightElevation * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 2
                        If isStep(tm, corridorState, firstTop) Then
                            _blocksCount -= 1
                        End If
                        firstTop += 0.001
                    End While
                    'доп.условие2: если на расстоянии до первого скачка блоков проектный профиль ниже облицовочного блока
                    Dim firstStepStation As Double
                    firstStepAtCurrRegion(tm, corridorState, firstStepStation)
                    Dim elevAtFirstStep = elevationTarget.GetElevation(oCurrentAlignmentId, firstStepStation)
                    If (elevAtFirstStep - oOrigin.Elevation) < (_blocksCount * blockStep) Then
                        _blocksCount -= 1
                    End If
                End If

                'условие для переопределения высоты облицовки
                If isStep(tm, corridorState, corridorState.CurrentStation) And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation + 0.001 Then
                    'вспомогательные вектора до и после скачка для оценки направления проектного профиля
                    Dim beforeStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation - 0.01)
                    Dim afterStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation + 0.01)
                    'сравниваем текущую высоту по блокам и высоту луча(общую высоту стенки)
                    If beforeStep < afterStep Then
                        Dim dif As Integer = (afterStep - oOrigin.Elevation - _blocksCount * dBlockHeight) * 1000 \ (dBlockHeight * 1000)
                        _blocksCount += dif
                    ElseIf beforeStep > afterStep Then
                        Dim dif As Integer = Math.Abs((_blocksCount * dBlockHeight - (afterStep - oOrigin.Elevation)) * 1000 \ (dBlockHeight * 1000)) + 1
                        _blocksCount -= dif
                    Else
                        Throw New Exception("что-то неладное")
                    End If

                End If
                rows = _blocksCount
            End If

            If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                Dim layersCount As Integer = RE520_count + RE540_count + RE560_count + RE570_count + RE580_count
                createSoilAddStations(tm, corridorState, elevationTarget, oBaseElevation, gStep, layersCount, odualR, odualRCountLow, odualRCountUp, odualRtoMiddle)
                createPipeAddStations(tm, corridorState, oPipeStep, oPipeSlope) 'доп сечения для трубы
            End If

            'Определяем есть ли профиль ТЫЛЬНОЙ стороны и его значение
            Dim backTarget As SlopeElevationTarget
            Try
                backTarget = oParamsElevationTarget.Value("BackProf")
            Catch
                backTarget = Nothing
            End Try

            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double

            If Not backTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallBackElevation = backTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallBackProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "BackProf", "RetainWallVertical")
                End Try
            End If
            'Определяем есть ли профиль ЛИЦЕВОЙ стороны и его значение
            Dim frontTarget As SlopeElevationTarget
                Try
                    frontTarget = oParamsElevationTarget.Value("FrontProf")
                Catch
                    frontTarget = Nothing
                End Try

                Dim hasWallFrontProfile As Boolean = False
                Dim dWallFrontElevation As Double

                If Not frontTarget Is Nothing Then
                    'get elevation on elevationTarget
                    Try
                        dWallFrontElevation = frontTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                        hasWallFrontProfile = True
                    Catch
                        Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "FrontProf", "RetainWallVertical")
                    End Try
                End If
                'Проверяем наличие ступеней в начеле и конце участка
                'BaseSteps(corridorState, oFStep, startStep, endStep, beforeNameRegion, afterNameRegion)
                'paramsLong.Item("BlocksCount").Value = rows
                Dim lowerLayerH As Double = oBaseElevation
                Dim levelWidth = 0.35 'ширина выравнивающего слоя по облицовке
                '--------------------------------
                'построение конструкции в сечении
                '--------------------------------
                Dim pointToSoil As New PointInMem
                Dim pointToSubbase As New PointInMem 'точка для вставки щебеночной подготовки
                Dim zeroPoint As New PointInMem With {
                    .Offset = flip * (baseL1 + toothL1),
                    .Elevation = 0
                    }
                Dim pointToFound As New PointInMem With {
                    .Offset = flip * ((baseL1 + baseL2 + toothL1 + cutL1) / 2 - toothL1 / 2),
                    .Elevation = 0
                    }
                Foundation(corridorState, flip, widthF, heightF, prepHeight, geotextileOverlap, lowerLayerH, dWallWidthOffset, hasWallOffsetTarget, hasGtxtLower, pointToFound, pointToSubbase)
                'создание облицовочных блоков
                createCladdingMB(corridorState, dWallHeightElevation, flip, rows, blockStep, dH, levelWidth, zeroPoint, pointToSoil)
                pointToSoil.Offset += flip * (baseL1 + toothL1) 'т.к. точка вставки = оси раскладки блока, смещаем точку на величину (горизонтальную) зуба+задней стороны
                pointToSoil.Elevation -= toothH1

                'создание армогрунта
                If frontTarget Is Nothing Then
                    dWallFrontElevation = dWallHeightElevation
                End If
                wallCreate(corridorState, dWallFrontElevation, dWallWidthOffset, dWallBackElevation, oGridWidth, gStep,
                           lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope, geotextileOverlap,
                           flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                           rows, blockStep, hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil, odualR, odualRCountLow, odualRCountUp, odualRtoMiddle)
                'CreateBaseWithSteps(corridorState, )

            Else 'для представления шаблона конструкции
                '----------------------------------
                'строим шаблон конструкции
                '----------------------------------
                Dim levelWidth = 0.35
            Dim levelH = 0.1
            'Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH
            Dim dWallHeightElevation As Double = oBaseElevation + (RE520_count + RE540_count + RE560_count + RE570_count + RE580_count) * gStep + levelH - gStep * (odualRCountLow + odualRCountUp)
            Dim blockLayers As Integer = dWallHeightElevation * 1000 \ blockStep * 1000
            Dim hasWallOffsetTarget As Boolean = False
            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double
            Dim lowerLayerH As Double = oBaseElevation

            Dim pointToSoil As New PointInMem  'точка для вставки армогрунта
            Dim pointToSubbase As New PointInMem 'точка для вставки щебеночной подготовки
            Dim pointToFound As New PointInMem With {
                .Offset = flip * ((baseL1 + baseL2 + toothL1 + cutL1) / 2 - toothL1 / 2),
                .Elevation = 0
                }
            'создание облицовочных блоков
            Foundation(corridorState, flip, widthF, heightF, prepHeight, geotextileOverlap, lowerLayerH, dWallWidthOffset, hasWallOffsetTarget, hasGtxtLower, pointToFound, pointToSubbase)
            createCladdingMB(corridorState, dWallHeightElevation, flip, blockLayers, blockStep, dH, levelWidth, oOrigin, pointToSoil)

            pointToSoil.Offset += flip * (baseL1 + toothL1) 'т.к. точка вставки = оси раскладки блока, смещаем точку на величину (горизонтальную) зуба+задней стороны
            pointToSoil.Elevation -= toothH1

            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation, oGridWidth, gStep,
                       lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope, geotextileOverlap,
                       flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                       blockLayers, blockStep, hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil, odualR, odualRCountLow, odualRCountUp, odualRtoMiddle)
        End If
        ' Обновляем входные параметры (если требуется)
        Dim param As IParam

        'param = paramsDouble.Add("Горизонтальное смещение", oHorizStep)
        'param = paramsDouble.Add("Наклон лицевой грани", oFaceAngle)
        param = paramsDouble.Add("Длина георешеток", oGridWidth)
        param = paramsDouble.Add("Шаг георешеток", gStep)
        param = paramsDouble.Add("Заложение дренажных призм", oDrenageSlope)
        param = paramsDouble.Add("Ширина дренажных призм", oDrenageOffset)
        param = paramsDouble.Add("Отступ первого слоя георешетки", oBaseElevation)
        param = paramsDouble.Add("Перехлест геотекстиля", geotextileOverlap)
        param = paramsLong.Add(Utilities.Side, side)
        param = paramsLong.Add("Отступ первого слоя дренажа", oDrenageElevation)
        param = paramsLong.Add("Кол-во RE580", RE580_count)
        param = paramsLong.Add("Кол-во RE570", RE570_count)
        param = paramsLong.Add("Кол-во RE560", RE560_count)
        param = paramsLong.Add("Кол-во RE540", RE540_count)
        param = paramsLong.Add("Кол-во RE520", RE520_count)
        param = paramsString.Add("Имя участка", oSubAsName)
        param = paramsDouble.Add("BlocksDeltaH", dH)
        param = paramsDouble.Add("BlockLength", dL)
        param = paramsDouble.Add("Толщина Подготовки", prepHeight)
        param = paramsDouble.Add("Уклон дренажной трубы", oPipeSlope)
        param = paramsDouble.Add("Шаг дренажных выпусков", oPipeStep)
        param = paramsDouble.Add("Диаметр дренажной трубы", oPipeD)
        param = paramsBool.Add("Геотекстиль в основании", hasGtxtLower)
        param = paramsBool.Add("Двойное армирование", odualR)
        param = paramsLong.Add("Кол-во нижних рядов блоков двойного армирования", odualRCountLow)
        param = paramsLong.Add("Кол-во верхних рядов блоков двойного армирования", odualRCountUp)
        param = paramsBool.Add("Выпуск герешетки из середины блока (выше двойного геоармирования)", odualRtoMiddle)
        'param = paramsLong.Add("BlocksAboveGrid", bAboveGrid)
        'param = paramsLong.Add("bCount", blockCount)
        'param = paramsDouble.Add("bHeight", blockHeight)
    End Sub

#Region "Создание армогрунта"
    'создание конструкции
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal wallWidth As Double,
                           ByVal wallBackHeight As Double,
                           ByVal gridWidth As Double,
                           ByVal verticalStep As Double,
                           ByVal baseLayerStep As Double,
                           ByVal drenageElevLayer As Long,
                           ByVal drenageWidth As Double,
                           ByVal drenageSlope As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal flipValue As Double,
                           ByVal RE520Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE580Count As Long,
                           ByVal blocksCount As Integer,
                           ByVal blockHeight As Double,
                           ByVal hasTargetOffset As Boolean,
                           ByVal hasTargetElevation As Boolean,
                           ByVal pipeStep As Double,
                           ByVal pipeSlope As Double,
                           ByVal pipeDiameter As Double,
                           ByVal startInputPoint As PointInMem,
                           ByVal IsDualReinf As Boolean,
                           ByVal dualReinfCountLow As Long,
                           ByVal dualReinfCountUp As Long,
                           ByVal IsAboveDualRtoMid As Boolean
                           )
        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem
        Dim elevatP As Double = startInputPoint.Elevation 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = startInputPoint.Offset
        insertPoint.Offset = offsetP 'присваиваем значения опорной точке
        insertPoint.Elevation = elevatP

        'определим кол-во слоев
        Dim layers As Integer
        layers = RE580Count + RE570Count + RE560Count + RE540Count + RE520Count
        Dim allLayers = layers
        'условие кол-ва слоев георешеток НЕ должно быть выше облицовки
        If layers > blocksCount And blocksCount < dualReinfCountLow Then 'условие 1: если блоков меньше чем необходимо для нижнего двойного армирования
            layers = blocksCount * 2
        ElseIf layers > blocksCount And blocksCount >= dualReinfCountLow And blocksCount < (allLayers - dualReinfCountUp * 2) - dualReinfCountLow Then 'условие 2: если блоки выше нижнего двойного армирования, но ниже верхнего
            layers = blocksCount + dualReinfCountLow
        ElseIf layers > blocksCount And blocksCount >= (allLayers - dualReinfCountUp * 2) - dualReinfCountLow Then 'условие 3: если блоки выше выше низа верхнего двойного армирования
            layers = blocksCount + dualReinfCountLow + (blocksCount - (allLayers - dualReinfCountUp * 2 - dualReinfCountLow))
        End If

        Dim geotextileName As String = "Геотекстиль"
        Dim soilName As String = "Дренирующий грунт"
        Dim drenageName As String = "Щебень дренажной призмы"
        Dim gridNameRE520 As String = "Георешетка RE520"
        Dim gridNameRE540 As String = "Георешетка RE540"
        Dim gridNameRE560 As String = "Георешетка RE560"
        Dim gridNameRE570 As String = "Георешетка RE570"
        Dim gridNameRE580 As String = "Георешетка RE580"

        Dim isLast As Boolean = False
        Dim isDrenLayers As Boolean = False
        Dim isFirst As Boolean = True
        Dim i As Integer = 0

        'песок ниже первой георешетки
        createLowerLayer(corridorState, soilName, wallWidth, baseLayerStep, flipValue, insertPoint, hasTargetOffset)
        elevatP += baseLayerStep
        'offsetP += baseLayerStep * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        i += 1
        'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
        Dim reinfStep As Double
        Do While layers > i

            If IsDualReinf And i < dualReinfCountLow * 2 Then 'условия для слоев имеющих двойное армирование и меньших указаного значения двойного армирования(нижнего)
                reinfStep = verticalStep / 2
            ElseIf IsDualReinf And i > allLayers - dualReinfCountUp * 2 Then
                reinfStep = verticalStep / 2
            Else 'если нет двойного армирования или выше значения двойного армирования
                reinfStep = verticalStep 'высота вертикального шага слоя георешеток
            End If
            If IsDualReinf And i = dualReinfCountLow * 2 And IsAboveDualRtoMid Then 'условие для смещения (чтобы выпуск был из середины блока) для слоев выше двойного армирования(нижнего)
                reinfStep = verticalStep - baseLayerStep
            ElseIf IsDualReinf And i = allLayers - dualReinfCountUp * 2 And dualReinfCountUp > 0 Then
                reinfStep = verticalStep - baseLayerStep
            End If

            'testPoint = testPoints.Add(offsetP, elevatP, i.ToString())
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            If i <= drenageElevLayer Then 'слои ниже дренажной призмы
                If i = drenageElevLayer Then
                    isLast = True
                    If layers > i Then
                        isDrenLayers = True
                    End If
                End If
                createNonDrenageLayer(corridorState, soilName, wallWidth, reinfStep, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, isLast, isDrenLayers)
            Else 'If drenageElevLayer < i And i < layers Then 'слои с дренажной призмой
                createDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                isFirst = False
            End If
            gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
            elevatP += reinfStep
            'offsetP += dX
            i += 1
        Loop
        reinfStep = verticalStep
        insertPoint.Offset = offsetP
        insertPoint.Elevation = elevatP
        'добавляем еще один слой решетки
        gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
        'проводим анализ оставшегося пространства

        Dim reminder As Double = wallHeight - elevatP

        ' Защита: если reminder <= 0 — верхний слой не строим совсем
        If reminder <= 0 Then
            ' всё пространство уже закрыто слоями цикла или стена убывает
            ' ничего не делаем
        ElseIf i <= drenageElevLayer Then
            If reminder > reinfStep Then
                createNonDrenageLayer(corridorState, soilName, wallWidth, reinfStep,
            drenageWidth, flipValue, insertPoint, geotxtOverlap,
            geotextileName, hasTargetOffset, isLast, isDrenLayers)
                reminder -= reinfStep
                elevatP += reinfStep
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
            End If
            ' Дополнительная защита: reminder всё ещё может быть ≈0
            If reminder > 0.001 Then
                createLastNonDrenageLayer(corridorState, soilName, wallWidth, reminder,
            wallBackHeight, flipValue, insertPoint, geotxtOverlap,
            geotextileName, hasTargetOffset, hasTargetElevation)
            End If
        Else
            If reminder > reinfStep Then
                createDrenageLayer(corridorState, drenageName, soilName, geotextileName,
            wallWidth, reinfStep, drenageWidth, drenageSlope, flipValue,
            insertPoint, geotxtOverlap, hasTargetOffset, isFirst,
            pipeStep, pipeSlope, pipeDiameter)
                reminder -= reinfStep
                elevatP += reinfStep
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
            End If
            If reminder > 0.001 Then
                createLastDrenageLayer(corridorState, drenageName, soilName, geotextileName,
            wallWidth, reinfStep, wallBackHeight, drenageWidth, drenageSlope,
            flipValue, insertPoint, geotxtOverlap, hasTargetOffset,
            hasTargetElevation, isFirst, reminder)
            End If
        End If



        'Dim reminder As Double = wallHeight - elevatP 'остаток
        'If i <= drenageElevLayer Then
        '    If reminder > reinfStep Then
        '        createNonDrenageLayer(corridorState, soilName, wallWidth, reinfStep, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, isLast, isDrenLayers)
        '        reminder -= reinfStep
        '        elevatP += reinfStep
        '        insertPoint.Offset = offsetP
        '        insertPoint.Elevation = elevatP
        '    End If
        '    createLastNonDrenageLayer(corridorState, soilName, wallWidth, reminder, wallBackHeight, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, hasTargetElevation)
        'Else
        '    If reminder > reinfStep Then
        '        createDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
        '        reminder -= reinfStep
        '        elevatP += reinfStep
        '        insertPoint.Offset = offsetP
        '        insertPoint.Elevation = elevatP
        '    End If
        '    createLastDrenageLayer(corridorState, drenageName, soilName, geotextileName, wallWidth, reinfStep, wallBackHeight, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, hasTargetOffset, hasTargetElevation, isFirst, reminder)
        'End If


    End Sub
    'создание георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
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

        Dim gridF As String = linkName + "_лицевая"

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, gridF)
        gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth * flipValue, pointToInsert.Elevation, linkName + "_тыльная")
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
    'добавление соответствующего слоя георешетки
    Private Sub gridlayers(ByVal corridorstate As CorridorState,
                           ByVal i As Integer,
                           ByVal insertPoint As PointInMem,
                           ByVal flipValue As Double,
                           ByVal gridWidth As Double,
                           ByVal gridNameRE580 As String,
                           ByVal gridNameRE570 As String,
                           ByVal gridNameRE560 As String,
                           ByVal gridNameRE540 As String,
                           ByVal gridNameRE520 As String,
                           ByVal RE580Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE520Count As Long
                           )
        Try 'логика присваивания названия слоя
            If i <= RE580Count Then
                'linkName = subAsName + " " + i.ToString + "_RE580"
                createGeogrid(corridorstate, gridNameRE580, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count < i And i <= RE580Count + RE570Count Then
                'linkName = subAsName + " " + i.ToString + "_RE570"
                createGeogrid(corridorstate, gridNameRE570, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count < i And i <= RE580Count + RE570Count + RE560Count Then
                'linkName = subAsName + " " + i.ToString + "_RE560"
                createGeogrid(corridorstate, gridNameRE560, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count Then
                'linkName = subAsName + " " + i.ToString + "_RE540"
                createGeogrid(corridorstate, gridNameRE540, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count + RE540Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count + RE520Count Then
                'linkName = subAsName + " " + i.ToString + "_RE520"
                createGeogrid(corridorstate, gridNameRE520, gridWidth, flipValue, insertPoint)
            End If
        Catch
            Utilities.RecordWarning(corridorstate, CorridorError.None, "no reinforcement", "ReinfSoilArray")
        End Try
    End Sub

    'создание самого нижнего слоя
    Private Sub createLowerLayer(ByVal corridorState As CorridorState,
                                 ByVal soilName As String,
                                 ByVal soilWidth As Double,
                                 ByVal layerHeight As Double,
                                 ByVal flipValue As Double,
                                 ByVal pointToInsert As PointInMem,
                                 ByVal hasTargetOffset As Boolean
                                 )
        '----------------------------
        'sand before first grid
        '----------------------------
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fSOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'firstStepOffset
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        Dim sandLinkName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "Низ дренирующего грунта")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
    End Sub
    'создание слоя засыпки дренирующим грунтом
    Private Sub createNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal drenageWidth As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal isLastlayer As Boolean,
                                      ByVal isAnyDrenageLayers As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек,связей и форм
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        'Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        'Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        'вычисляем вспомогательные параметры
        Dim gtxtOffset = geotxtOverlap * flipValue
        'создание коллекций для точек и связей
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4

        geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset + 0.001 * flipValue, sandPoint2.Elevation, geotextilePointName3)
        'проверка наличия сверху слоя с дренажом
        If isLastlayer And isAnyDrenageLayers Then
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset + drenageWidth * flipValue, sandPoint2.Elevation, geotextilePointName4)
        Else
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        End If
        'Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        'Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, geotextileName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, geotextileName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, geotextileName)
    End Sub
    'создание слоя засыпки с пристеночным дренажом
    Private Sub createDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal pipeStep As Double,
                                   ByVal pipeSlope As Double,
                                   ByVal pipeDiameter As Double
                                   )
        'вычисляем вспомогательные параметры
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fOffset As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        Dim dOffset As Double = layerHeight * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 4
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset, drPoint1.Elevation + layerHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        ' Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        ' Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drenageName)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, drenageName)
        Dim drLink3 = drenageLinks.Add(drPoint3, drPoint4, drenageName)
        Dim drLink4 = drenageLinks.Add(drPoint1, drPoint4, drenageName)
        'create shape for gravel layer
        Dim drShapeName As String = "" 'subAsName & "_" & drenageName '& CStr(grNumber)
        Dim drShape = drenageShapes.Add(drLink1, drLink2, drLink3, drLink4, drenageName)
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        'Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        'Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(drPoint4.Offset, drPoint4.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(drPoint3.Offset, drPoint3.Elevation, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then

            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(drPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(drPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, "")
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4
        'проверка наличия сверху слоя с дренажом
        If isFirstlayer Then
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        Else
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset + dOffset, sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)

        'Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        'Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

        If isFirstlayer Then 'создание дренажной трубы и геомембраны в основании пристеночного дренажа
            Dim dPoint As Point
            createPipeAxis(corridorState, pipeStep, pipeSlope, pointToInsert, dPoint, pipeDiameter, flipValue)
            createPipe(corridorState, dPoint, pipeDiameter)
            createGeomembrane(corridorState, pointToInsert, pipeDiameter, drenageWidth, layerHeight, drenageSlope, flipValue)
        End If
    End Sub
    'создание слоя засыпки с пристеночным дренажом самого верхнего слоя (с возможностью задать перехлест геотекстиля с нижне лежащим слоем) 
    Private Sub createLastDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double, 'высота стандартного слоя
                                   ByVal layerHeightBack As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal hasTargetElev As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal lastHeight As Double 'высота последнего слоя
                                   )

        'вычисляем вспомогательные параметры
        'Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        'Dim fOffset As Double = lastHeight * Math.Tan(faceSlope) * flipValue 'last layer FaceOffset
        Dim dOffset As Double = lastHeight * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        Dim gtxtLow As Double = (layerHeight * drenageSlope) * flipValue
        Dim gtxtTop As Double = (drenageWidth + dOffset) * flipValue
        ' Dim fOffsetLow As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "Пристеночный дренаж верх по лицевой грани" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "Пристеночный дренаж верх по засыпке" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "Пристеночный дренаж низ по засыпке" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 4
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset, drPoint1.Elevation + lastHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        'Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        'Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drenageName)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, "Верх дренажа")
        Dim drLink3 = drenageLinks.Add(drPoint3, drPoint4, drenageName)
        Dim drLink4 = drenageLinks.Add(drPoint1, drPoint4, drenageName)
        'create shape for gravel layer
        'Dim drShapeName As String = subAsName & "_" & drenageName '& CStr(grNumber)
        Dim drShape = drenageShapes.Add(drLink1, drLink2, drLink3, drLink4, drenageName)
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "Засыпка по лицевой стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "Засыпка по тыльной стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        ' Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        ' Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(drPoint4.Offset, drPoint4.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(drPoint3.Offset, drPoint3.Elevation, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(drPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(drPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        If hasTargetElev Then
            If layerHeightBack >= sandPoint1.Elevation Then
                sandPoint3.Elevation = layerHeightBack
            Else
                sandPoint3.Elevation = sandPoint2.Elevation
            End If
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, "")
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Верх дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'Dim geotextilePoint5 As Point
        'Dim geotextilePoint6 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link
        'Dim geotextileLink4 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4
        'Dim geotextilePointName5 As String = subAsName & "_" & "geotextile" & 5
        'Dim geotextilePointName6 As String = subAsName & "_" & "geotextile" & 6

        'проверка наличия сверху слоя с дренажом
        If isFirstlayer Then
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        Else
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + (gtxtOffset + gtxtLow), sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        'geotextilePoint5 = geotxtPoints.Add(geotextilePoint4.Offset, geotextilePoint4.Elevation, geotextilePointName5)
        ' geotextilePoint6 = geotxtPoints.Add(geotextilePoint5.Offset - (gtxtOffset + gtxtTop), geotextilePoint5.Elevation, geotextilePointName6)


        '  Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        ' Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"
        ' Dim geotextileLinkName3 As String = subAsName & "_" & "geotextileTop"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

        'geotextileLink4 = geotxtLinks.Add(geotextilePoint5, geotextilePoint6, geotextileLinkName3)
    End Sub
    'создание слоя засыпки дренирующим грунтом самого верхнего слоя
    Private Sub createLastNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal layerHeightBack As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal hasTargetElev As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек,связей и форм
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "Засыпка по лицевой стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "Засыпка по тыльной стороне" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'проверка наличия цели для отметки(тыльной точки стены)
        If hasTargetElev Then
            If layerHeightBack >= sandPoint1.Elevation Then
                sandPoint3.Elevation = layerHeightBack
            Else
                sandPoint3.Elevation = sandPoint2.Elevation
            End If
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Верх дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        'вычисляем вспомогательные параметры
        Dim gtxtOffset = geotxtOverlap * flipValue
        'создание коллекций для точек и связей
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4

        geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset + 0.001 * flipValue, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, geotextileName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, geotextileName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, geotextileName)
    End Sub
    '
    Private Sub createPipeAxis(ByVal corridorState As CorridorState, ByVal pipeStep As Double, ByVal pipeSlope As Double, ByVal insertPoint As PointInMem, ByRef axisPoint As Point, pipeDiam As Double, flip As Double)
        Dim dPointCollection As PointCollection
        dPointCollection = corridorState.Points
        'находим переменные задающие положение трубы в пространстве (вертикальное смещение)
        'максимально возможная отметка относительно нуля 
        Dim tH = pipeStep * pipeSlope
        'дельта отметки для рассматриваемого сечения
        Dim dH = ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) Mod 2 * pipeStep) * pipeSlope
        'направление в котором стоит откладывать дельту в текущем сечении
        Dim dir = Math.Sin(PI / 2 + ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) \ pipeStep) * PI)
        'определяем отметку

        Dim oPipeElev As Double
        oPipeElev = Math.Abs(tH - dH) + pipeDiam / 2

        axisPoint = dPointCollection.Add(insertPoint.Offset + (pipeDiam / 2 + 0.05) * flip, insertPoint.Elevation + oPipeElev, "Ось дренажной трубы")

    End Sub
    Private Sub createPipe(ByVal corridorState As CorridorState, cPoint As Point, pDiam As Double)
        Dim pipePoints As PointCollection
        pipePoints = corridorState.Points
        Dim pipeLinks As LinkCollection
        pipeLinks = corridorState.Links
        Dim pipeShapes As ShapeCollection
        pipeShapes = corridorState.Shapes
        Dim P1 As Point
        Dim P2 As Point
        Dim L1 As Link
        Dim S1 As Autodesk.Civil.DatabaseServices.Shape

        Dim i As Double = 0
        Dim circleStep = PI / 6
        Dim links As New List(Of Link)
        Do While i < 2 * PI
            If i <> 1.5 * PI Then
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            Else
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "Низ дренажной трубы")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            End If
            links.Add(L1)
            i += circleStep
        Loop
        S1 = pipeShapes.Add(links.ToArray(), "Дренажная труба")
    End Sub
    Private Sub createGeomembrane(corridorState As CorridorState, ByVal insertPoint As PointInMem, pDiam As Double, drenageOffset As Double, layerHeight As Double, layerSlope As Double, flip As Double)

        Dim membranePoints As PointCollection
        membranePoints = corridorState.Points
        Dim membraneLinks As LinkCollection
        membraneLinks = corridorState.Links

        Dim membraneName As String = "Геомембрана"

        Dim P1 As Point
        Dim P2 As Point
        Dim P3 As Point
        Dim P4 As Point
        Dim L1 As Link
        Dim L2 As Link
        Dim L3 As Link

        Dim membraneLow = insertPoint.Elevation
        Dim membraneHeightFace = 0.3
        Dim layerTan = 1 / layerSlope

        P1 = membranePoints.Add(insertPoint.Offset + 0.001 * flip, insertPoint.Elevation + membraneHeightFace, "")
        P2 = membranePoints.Add(insertPoint.Offset, insertPoint.Elevation, "")
        P3 = membranePoints.Add(P2.Offset + drenageOffset * flip, P2.Elevation, "")
        P4 = membranePoints.Add(P3.Offset + layerHeight * layerSlope * flip, P3.Elevation + layerHeight, "")


        L1 = membraneLinks.Add(P1, P2, membraneName)
        L2 = membraneLinks.Add(P2, P3, membraneName)
        L3 = membraneLinks.Add(P3, P4, membraneName)
    End Sub
    Public Sub createPipeAddStations(tm As DBTransactionManager, corridorState As CorridorState, pipeStep As Double, pipeSlope As Double)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находи пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt
        Dim sectionsToAdd As New List(Of Double)
        Dim tH = pipeStep * pipeSlope
        Do While stationCurr < endSt
            Dim dH = ((stationCurr - corridorState.CurrentRegionStartStation) Mod pipeStep) * pipeSlope
            Dim remainder = tH - dH
            If Math.Abs(remainder) < 0.0001 Then
                sectionsToAdd.Add(stationCurr)
                stationCurr += 0.1
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
                            Dim description = "доп.сечения дренажной трубы " + baseline.Name
                            If info.Description = description Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff = sectionsToAdd.Except(assemblyStations)
                        For Each station In diff
                            Try
                                reg.AddStation(station, "доп.сечения дренажной трубы " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub

    ''' <summary>
    ''' Добавляет пары доп. сечений (±0.0005 м) в пикетах где продольный профиль
    ''' пересекает уровни георешёток. Уровни строятся той же логикой что и в wallCreate:
    ''' baseLayerStep снизу, затем шаг verticalStep (или verticalStep/2 в зонах
    ''' двойного армирования), с поправкой смещения при переходе через зону.
    ''' </summary>
    Public Sub createSoilAddStations(tm As DBTransactionManager,
                                  corridorState As CorridorState,
                                  target As SlopeElevationTarget,
                                  baseLayerStep As Double,
                                  verticalStep As Double,
                                  allLayers As Integer,
                                  IsDualReinf As Boolean,
                                  dualReinfCountLow As Long,
                                  dualReinfCountUp As Long,
                                  IsAboveDualRtoMid As Boolean)

        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)

        Dim startSt As Double = corridorState.CurrentRegionStartStation
        Dim endSt As Double = corridorState.CurrentRegionEndStation

        ' --- 1. Строим список ВЫСОТ уровней георешёток (относительно подошвы) ---
        ' Повторяем логику reinfStep из wallCreate шаг-в-шаг.
        Dim levelHeights As New List(Of Double)
        Dim h As Double = baseLayerStep      ' первый уровень — над нижним слоем песка
        levelHeights.Add(h)

        Dim i As Integer = 1
        Do While i < allLayers
            Dim reinfStep As Double

            If IsDualReinf AndAlso i < dualReinfCountLow * 2 Then
                reinfStep = verticalStep / 2
            ElseIf IsDualReinf AndAlso i > allLayers - dualReinfCountUp * 2 Then
                reinfStep = verticalStep / 2
            Else
                reinfStep = verticalStep
            End If

            ' Поправка смещения на стыке зоны двойного армирования (как в wallCreate)
            If IsDualReinf AndAlso i = dualReinfCountLow * 2 AndAlso IsAboveDualRtoMid Then
                reinfStep = verticalStep - baseLayerStep
            ElseIf IsDualReinf AndAlso i = allLayers - dualReinfCountUp * 2 AndAlso dualReinfCountUp > 0 Then
                reinfStep = verticalStep - baseLayerStep
            End If

            h += reinfStep
            levelHeights.Add(h)
            i += 1
        Loop

        ' --- 2. Ищем пересечения профиля с каждым уровнем по сегментам ---
        Dim profileH As Autodesk.Civil.DatabaseServices.Profile =
        tm.GetObject(target.TargetId, OpenMode.ForRead)

        Dim sectionsToAdd As New List(Of Double)

        For Each ent In profileH.Entities

            If ent.EntityType <> ProfileEntityType.Tangent Then Continue For
            If ent.StartElevation = ent.EndElevation Then Continue For
            If ent.EndStation <= startSt OrElse ent.StartStation >= endSt Then Continue For

            Dim segStart As Double = Math.Max(ent.StartStation, startSt)
            Dim segEnd As Double = Math.Min(ent.EndStation, endSt)
            Dim segLen As Double = ent.EndStation - ent.StartStation
            Dim slope As Double = (ent.EndElevation - ent.StartElevation) / segLen  ' абс. отметка / пикет

            ' Высота стены (отметка профиля − отметка оси) на границах рабочего диапазона
            Dim wallHStart As Double = ent.StartElevation + slope * (segStart - ent.StartStation) - origin.Elevation
            Dim wallHEnd As Double = ent.StartElevation + slope * (segEnd - ent.StartStation) - origin.Elevation

            Dim hMin As Double = Math.Min(wallHStart, wallHEnd)
            Dim hMax As Double = Math.Max(wallHStart, wallHEnd)

            For Each levelH As Double In levelHeights

                ' Уровень должен попадать строго внутрь диапазона высот сегмента
                If levelH <= hMin OrElse levelH >= hMax Then Continue For

                ' Пикет где высота стены = levelH:
                ' levelH = ent.StartElevation + slope*(st − ent.StartStation) − origin.Elevation
                Dim stationExact As Double = ent.StartStation +
                (levelH + origin.Elevation - ent.StartElevation) / slope

                If stationExact <= segStart OrElse stationExact >= segEnd Then Continue For

                Dim stBefore As Double = Math.Round(stationExact - 0.0005, 4)
                Dim stAfter As Double = Math.Round(stationExact + 0.0005, 4)

                If stBefore > startSt AndAlso Not sectionsToAdd.Contains(stBefore) Then sectionsToAdd.Add(stBefore)
                If stAfter < endSt AndAlso Not sectionsToAdd.Contains(stAfter) Then sectionsToAdd.Add(stAfter)

            Next
        Next

        sectionsToAdd.Sort()

        ' --- 3. Запись в коридор с защитой от эха ---
        Dim corridor As Corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim descSoil As String = "скачок засыпки "

        For Each b As Baseline In corridor.Baselines
            If corridorState.CurrentProfileId <> b.ProfileId Then Continue For

            For Each reg As BaselineRegion In b.BaselineRegions
                If reg.StartStation <> startSt AndAlso
               reg.EndStation <> corridorState.CurrentRegionEndStation Then Continue For

                Dim settings = reg.AppliedAssemblySetting
                Dim infos = settings.AdditionalAppliedAssemblies

                Dim existingSoil As New List(Of Double)
                For Each info In infos
                    If info.Description = descSoil & b.Name Then existingSoil.Add(Math.Round(info.Station, 4))
                Next
                existingSoil.Sort()

                Dim identical As Boolean = (existingSoil.Count = sectionsToAdd.Count)
                If identical Then
                    For k As Integer = 0 To sectionsToAdd.Count - 1
                        If Math.Abs(existingSoil(k) - sectionsToAdd(k)) > 0.0001 Then
                            identical = False : Exit For
                        End If
                    Next
                End If
                If identical Then Continue For

                For Each info In infos
                    If info.Description = descSoil & b.Name Then reg.DeleteStation(info.Station)
                Next

                Dim existingAll As New HashSet(Of Double)(reg.AppliedAssemblies.Stations)
                For Each st As Double In sectionsToAdd
                    If existingAll.Contains(st) Then Continue For
                    Try
                        reg.AddStation(st, descSoil & b.Name)
                    Catch
                    End Try
                Next

            Next
        Next
    End Sub




#End Region
#Region "Создание облицовки"
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

        Dim levLink1 = levelLinks.Add(levPoint1, levPoint2, "")
        Dim levLink2 = levelLinks.Add(levPoint2, levPoint3, "")
        Dim levLink3 = levelLinks.Add(levPoint1, levPoint4, "")
        Dim levLink4 = levelLinks.Add(levPoint4, levPoint5, "")
        Dim levLink5 = levelLinks.Add(levPoint5, levPoint6, "")
        Dim levLink6 = levelLinks.Add(levPoint3, levPoint6, "")

        Dim levLinks As Link() = {levLink1, levLink2, levLink6, levLink5, levLink4, levLink3}

        Dim levShape = levelShapes.Add(levLinks, levTopName)
    End Sub
    'метод для добавления шага блока
    ''' <summary>
    ''' Добавляет доп. сечения в пикетах скачка облицовки.
    ''' Аналитически по сегментам профиля — без сканирования.
    ''' Пара сечений на каждый скачок: на пикете границы блока и +0.001 от него.
    ''' </summary>
    Public Sub createAddStations(tm As DBTransactionManager,
                             corridorState As CorridorState,
                             blockStep As Double,
                             blockLength As Double,
                             target As SlopeElevationTarget)

        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)

        Dim startSt As Double = corridorState.CurrentRegionStartStation
        Dim endSt As Double = corridorState.CurrentRegionEndStation
        Dim sliceStep As Double = blockLength / 2  ' шаг сетки границ блоков

        ' Загружаем профиль для прямого доступа к сегментам
        Dim profileH As Autodesk.Civil.DatabaseServices.Profile =
        tm.GetObject(target.TargetId, OpenMode.ForRead)

        Dim sectionsToAdd As New List(Of Double)      ' пикеты "до скачка"
        Dim sectionsToAddStep As New List(Of Double)  ' пикеты "после скачка" (+0.001)

        ' Итерируем по сегментам профиля
        For Each ent In profileH.Entities

            ' Только наклонные тангенсы внутри области
            If ent.EntityType <> ProfileEntityType.Tangent Then Continue For
            If ent.StartElevation = ent.EndElevation Then Continue For
            If ent.EndStation <= startSt Then Continue For
            If ent.StartStation >= endSt Then Continue For

            ' Рабочий диапазон сегмента внутри области
            Dim segStart As Double = Math.Max(ent.StartStation, startSt)
            Dim segEnd As Double = Math.Min(ent.EndStation, endSt)
            Dim segLen As Double = ent.EndStation - ent.StartStation
            Dim slope As Double = (ent.EndElevation - ent.StartElevation) / segLen  ' м/м

            ' Отметки стены (относительно origin) на границах рабочего диапазона
            Dim elevAtSegStart As Double = ent.StartElevation +
            slope * (segStart - ent.StartStation) - origin.Elevation
            Dim elevAtSegEnd As Double = ent.StartElevation +
            slope * (segEnd - ent.StartStation) - origin.Elevation

            ' Диапазон кратных уровней blockStep попадающих в этот сегмент
            Dim levelMin As Double = Math.Min(elevAtSegStart, elevAtSegEnd)
            Dim levelMax As Double = Math.Max(elevAtSegStart, elevAtSegEnd)

            Dim firstLevel As Integer = CInt(Math.Ceiling(levelMin / blockStep))
            Dim lastLevel As Integer = CInt(Math.Floor(levelMax / blockStep))

            For level As Integer = firstLevel To lastLevel

                Dim levelElev As Double = level * blockStep  ' абсолютный уровень скачка (относительно origin)

                ' Точный пикет где профиль достигает этого уровня
                Dim stationExact As Double = ent.StartStation +
                (origin.Elevation + levelElev - ent.StartElevation) / slope

                ' Защита: пикет должен быть строго внутри рабочего диапазона
                If stationExact <= segStart OrElse stationExact >= segEnd Then Continue For

                ' Привязываем к ближайшей границе сетки блоков (кратной sliceStep от startSt)
                Dim distFromStart As Double = stationExact - startSt
                Dim gridIndex As Double = Math.Round(distFromStart / sliceStep)
                Dim stationSnapped As Double = startSt + gridIndex * sliceStep

                ' Проверяем что привязанный пикет внутри области и не дублирует уже найденный
                If stationSnapped <= startSt OrElse stationSnapped >= endSt Then Continue For
                If sectionsToAdd.Contains(stationSnapped) Then Continue For

                ' Определяем направление скачка: стена растёт или убывает?
                ' Если профиль идёт вниз (slope < 0) — скачок "вниз", сечение ДО границы блока
                ' Если профиль идёт вверх (slope > 0) — скачок "вверх", сечение НА границе блока
                If slope < 0 Then
                    ' стена убывает: скачок происходит когда профиль опускается ниже уровня
                    ' ставим сечение чуть раньше границы сетки и сразу после
                    sectionsToAdd.Add(stationSnapped)
                    sectionsToAddStep.Add(stationSnapped + 0.001)
                Else
                    ' стена растёт: скачок происходит когда профиль поднимается выше уровня
                    sectionsToAdd.Add(stationSnapped)
                    sectionsToAddStep.Add(stationSnapped + 0.001)
                End If

            Next ' level
        Next ' ent

        ' Записываем в коридор
        Dim corridor As Corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)

        For Each b As Baseline In corridor.Baselines
            If corridorState.CurrentProfileId = b.ProfileId Then

                For Each reg As BaselineRegion In b.BaselineRegions
                    If reg.StartStation = startSt OrElse
                   reg.EndStation = corridorState.CurrentRegionEndStation Then

                        ' Очищаем старые сечения облицовки
                        Dim settings = reg.AppliedAssemblySetting
                        For Each info In settings.AdditionalAppliedAssemblies
                            Dim d1 = "доп.сечения облицовочных блоков " & b.Name
                            Dim d2 = "скачок облицовки " & b.Name
                            If info.Description = d1 OrElse info.Description = d2 Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next

                        ' Добавляем только новые пикеты
                        Dim existingStations As Double() = reg.AppliedAssemblies.Stations
                        For Each st As Double In sectionsToAdd.Except(existingStations)
                            Try
                                reg.AddStation(st, "доп.сечения облицовочных блоков " & b.Name)
                            Catch
                            End Try
                        Next
                        For Each st As Double In sectionsToAddStep.Except(existingStations)
                            Try
                                reg.AddStation(st, "скачок облицовки " & b.Name)
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

    ''' <summary>
    ''' Добавляет доп. сечения по целевому профилю облицовки.
    ''' Ставит пару сечений (-0.0005 / +0.0005) на каждой границе сегментов
    ''' целевого профиля внутри области. Защита от эха: сравнивает с уже
    ''' существующими сечениями и пишет в коридор только при реальном изменении.
    ''' </summary>
    Public Sub createAddStationsForProfile(tm As DBTransactionManager,
                                        corridorState As CorridorState,
                                        target As SlopeElevationTarget)

        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)

        Dim startSt As Double = corridorState.CurrentRegionStartStation
        Dim endSt As Double = corridorState.CurrentRegionEndStation

        Dim profileH As Autodesk.Civil.DatabaseServices.Profile =
        tm.GetObject(target.TargetId, OpenMode.ForRead)

        ' --- Вычисляем нужный набор пикетов аналитически ---
        Dim sectionsToAdd As New List(Of Double)

        For Each ent In profileH.Entities

            ' Только наклонные тангенсы с границами внутри области
            If ent.EntityType <> ProfileEntityType.Tangent Then Continue For
            If ent.StartElevation = ent.EndElevation Then Continue For

            ' Граница сегмента — это точка излома профиля.
            ' Нам интересны StartStation каждого наклонного сегмента:
            ' именно здесь профиль меняет направление → нужна пара сечений.

            ' Начало сегмента (= конец предыдущего): ставим пару если внутри области
            Dim stStart As Double = ent.StartStation
            If stStart > startSt + 0.001 AndAlso stStart < endSt - 0.001 Then
                Dim stBefore As Double = Math.Round(stStart - 0.0005, 4)
                Dim stAfter As Double = Math.Round(stStart + 0.0005, 4)
                If Not sectionsToAdd.Contains(stBefore) Then sectionsToAdd.Add(stBefore)
                If Not sectionsToAdd.Contains(stAfter) Then sectionsToAdd.Add(stAfter)
            End If

            ' Конец сегмента: аналогично
            Dim stEnd As Double = ent.EndStation
            If stEnd > startSt + 0.001 AndAlso stEnd < endSt - 0.001 Then
                Dim stBefore As Double = Math.Round(stEnd - 0.0005, 4)
                Dim stAfter As Double = Math.Round(stEnd + 0.0005, 4)
                If Not sectionsToAdd.Contains(stBefore) Then sectionsToAdd.Add(stBefore)
                If Not sectionsToAdd.Contains(stAfter) Then sectionsToAdd.Add(stAfter)
            End If

        Next

        sectionsToAdd.Sort()

        ' --- Записываем в коридор только при реальном изменении (защита от эха) ---
        Dim corridor As Corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim descMain As String = "доп.сечения облицовочных блоков "
        Dim descStep As String = "скачок облицовки "

        For Each b As Baseline In corridor.Baselines
            If corridorState.CurrentProfileId <> b.ProfileId Then Continue For

            For Each reg As BaselineRegion In b.BaselineRegions
                If reg.StartStation <> startSt AndAlso
               reg.EndStation <> corridorState.CurrentRegionEndStation Then Continue For

                Dim settings = reg.AppliedAssemblySetting
                Dim infos = settings.AdditionalAppliedAssemblies

                ' Собираем уже существующие пикеты наших описаний
                Dim existingProfile As New List(Of Double)
                For Each info In infos
                    If info.Description = descMain & b.Name OrElse
                   info.Description = descStep & b.Name Then
                        existingProfile.Add(Math.Round(info.Station, 4))
                    End If
                Next
                existingProfile.Sort()

                ' --- Защита от эха: если наборы совпадают — ничего не делаем ---
                Dim identical As Boolean = (existingProfile.Count = sectionsToAdd.Count)
                If identical Then
                    For k As Integer = 0 To sectionsToAdd.Count - 1
                        If Math.Abs(existingProfile(k) - sectionsToAdd(k)) > 0.0001 Then
                            identical = False
                            Exit For
                        End If
                    Next
                End If
                If identical Then Continue For  ' реального изменения нет — коридор не трогаем

                ' --- Удаляем устаревшие сечения ---
                For Each info In infos
                    If info.Description = descMain & b.Name OrElse
                   info.Description = descStep & b.Name Then
                        reg.DeleteStation(info.Station)
                    End If
                Next

                ' --- Добавляем новые ---
                Dim existingAll As New HashSet(Of Double)(reg.AppliedAssemblies.Stations)
                For Each st As Double In sectionsToAdd
                    If existingAll.Contains(st) Then Continue For
                    Try
                        ' Чётные по индексу (0,2,4...) = "до излома", нечётные = "после"
                        Dim desc As String = If(sectionsToAdd.IndexOf(st) Mod 2 = 0,
                                           descMain & b.Name,
                                           descStep & b.Name)
                        reg.AddStation(st, desc)
                    Catch
                    End Try
                Next

            Next ' reg
        Next ' baseline
    End Sub


    'метод для определения пикета с первой в данной области ступенью(необходимо для проверки при понижении стены)
    Public Sub firstStepAtCurrRegion(tm As DBTransactionManager, corridorState As CorridorState, ByRef station As Double)
        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        Dim firstStat As Double
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'находим необходимое сечение
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "скачок облицовки " + baseline.Name
                            If info.Description = description Then
                                firstStat = info.Station
                                Exit For
                            End If
                        Next
                    End If
                Next
            End If
        Next
        station = firstStat
    End Sub
#End Region
#Region "Создание щебеночной подготовки"
    'метод для создания фундаментного блока
    Public Sub Foundation(corridorState As CorridorState,
                                 flip As Double,
                                 fWidth As Double,
                                 fHeight As Double,
                                 prepHeight As Double,
                                 geotxtOverlap As Double,
                                 baseLayerHeight As Double,
                                 soilWidth As Double,
                                 hasTargetOffset As Boolean,
                                 hasGeotextileLower As Boolean,
                                 insertPoint As PointInMem,
                                 ByRef outputPoint As PointInMem)
        Dim soilName As String = "Дренирующий грунт"
        Dim concLeveling As String = "Цементная подготовка"
        Dim foundationConcrete As String = "Бетон фундамента"
        Dim gidro As String = "Гидроизоляция фундамента"
        Dim geotextileName As String = "Геотекстиль"
        Dim plenka As String = "Полиэтиленовая пленка"
        Dim a As Double = Math.Atan(toothL1 / toothH1)

        '---------------------------------------------------------
        ' Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim foundPoints As PointCollection
        foundPoints = corridorState.Points
        Dim foundLinks As LinkCollection
        foundLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim preparePoints As PointCollection
        preparePoints = corridorState.Points
        Dim prepareLinks As LinkCollection
        prepareLinks = corridorState.Links
        '------------------------------------
        Dim gidroPoints As PointCollection
        gidroPoints = corridorState.Points
        Dim gidroLinks As LinkCollection
        gidroLinks = corridorState.Links
        '------------------------------------
        Dim foundatP1 As Point
        Dim foundatP2 As Point
        Dim foundatP3 As Point
        Dim foundatP4 As Point
        Dim foundatP5 As Point
        Dim foundatP6 As Point
        Dim foundatP7 As Point
        Dim prepP7 As Point
        Dim prepP8 As Point
        Dim prepP9 As Point
        Dim prepP10 As Point

        Dim gidroP1 As Point
        Dim gidroP2 As Point
        Dim gidroP3 As Point
        Dim gidroP4 As Point
        Dim gidroP5 As Point
        Dim gidroP6 As Point

        'Dim helpPoint As Point

        Dim foundatLink1 As Link
        Dim foundatLink2 As Link
        Dim foundatLink3 As Link
        Dim foundatLink4 As Link
        Dim foundatLink5 As Link
        Dim foundatLink6 As Link

        Dim prepLink1 As Link
        Dim prepLink2 As Link
        Dim prepLink3 As Link
        Dim prepLink4 As Link
        Dim prepLink5 As Link
        Dim prepLink6 As Link
        Dim prepLink7 As Link
        Dim prepLink8 As Link

        Dim gidroL1 As Link
        Dim gidroL2 As Link
        Dim gidroL3 As Link
        Dim gidroL4 As Link

        Dim foundShape As Shape
        Dim prepareShape As Shape
        '--------------------------------------------------------
        'создаем фундамент
        foundatP1 = foundPoints.Add(insertPoint.Offset - flip * Math.Cos(a) * prepHeight, insertPoint.Elevation - prepHeight, "")
        foundatP2 = foundPoints.Add(foundatP1.Offset - (fWidth / 2 - toothL1 / 2) * flip, foundatP1.Elevation, "")
        foundatP3 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation - (fHeight + toothH1), "")
        foundatP4 = foundPoints.Add(foundatP3.Offset + fWidth * flip, foundatP3.Elevation, "")
        foundatP5 = foundPoints.Add(foundatP4.Offset, foundatP4.Elevation + fHeight, "")
        foundatP6 = foundPoints.Add(foundatP1.Offset + toothL1 * flip, foundatP5.Elevation, "")

        foundatP7 = foundPoints.Add(foundatP3.Offset + fWidth / 2 * flip, foundatP3.Elevation, "Ось фундамента")

        Dim foundGidro As String() = {foundationConcrete, gidro}

        foundatLink1 = foundLinks.Add(foundatP1, foundatP2, foundationConcrete)
        foundatLink2 = foundLinks.Add(foundatP2, foundatP3, foundationConcrete)
        foundatLink3 = foundLinks.Add(foundatP3, foundatP4, foundationConcrete)
        foundatLink4 = foundLinks.Add(foundatP4, foundatP5, foundationConcrete)
        foundatLink5 = foundLinks.Add(foundatP5, foundatP6, foundationConcrete)
        foundatLink6 = foundLinks.Add(foundatP6, foundatP1, foundationConcrete)

        Dim fLinks As Link() = {foundatLink1, foundatLink2, foundatLink3, foundatLink4, foundatLink5, foundatLink6}

        foundShape = Shapes.Add(fLinks, foundationConcrete)

        'Dim off = foundatP1.Offset
        'Dim ele = foundatP1.Elevation

        'создаем подготовку под блоки
        prepP7 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation + prepHeight, "")
        prepP8 = foundPoints.Add(insertPoint.Offset, insertPoint.Elevation, "")
        prepP9 = foundPoints.Add(insertPoint.Offset + toothL1 * flip, insertPoint.Elevation - toothH1, "")
        prepP10 = foundPoints.Add(foundatP4.Offset, foundatP5.Elevation + prepHeight, "")

        'helpPoint = foundPoints.Add(foundatP7.Offset, foundatP2.Elevation, "")
        Dim levelGidro As String() = {concLeveling, gidro}

        prepLink1 = prepareLinks.Add(foundatP2, prepP7, concLeveling)
        prepLink2 = prepareLinks.Add(prepP7, prepP8, concLeveling)
        prepLink3 = prepareLinks.Add(prepP8, prepP9, concLeveling)
        prepLink4 = prepareLinks.Add(prepP9, prepP10, concLeveling)
        prepLink5 = prepareLinks.Add(prepP10, foundatP5, concLeveling)
        prepLink6 = prepareLinks.Add(foundatP5, foundatP6, concLeveling)
        prepLink7 = prepareLinks.Add(foundatP6, foundatP1, concLeveling)
        prepLink8 = prepareLinks.Add(foundatP1, foundatP2, concLeveling)

        Dim prepLinks As Link() = {prepLink1, prepLink2, prepLink3, prepLink4, prepLink5, prepLink6, prepLink7, prepLink8}
        prepareShape = Shapes.Add(prepLinks, concLeveling)
        'создаем гидроизоляцию
        gidroP1 = gidroPoints.Add(foundatP3.Offset - 0.001 * flip, foundatP3.Elevation, gidro + "1")
        gidroP2 = gidroPoints.Add(prepP7.Offset, prepP7.Elevation, "")
        gidroP3 = gidroPoints.Add(prepP8.Offset - flip * (cutL1 + baseL2), prepP8.Elevation, "")
        gidroP4 = gidroPoints.Add(foundatP4.Offset + 0.001 * flip, foundatP4.Elevation, gidro + "2")
        gidroP5 = gidroPoints.Add(prepP10.Offset, prepP10.Elevation, "")
        gidroP6 = gidroPoints.Add(prepP9.Offset + flip * baseL1, prepP9.Elevation, "")

        gidroL1 = gidroLinks.Add(gidroP1, gidroP2, gidro)
        gidroL2 = gidroLinks.Add(gidroP2, gidroP3, gidro)
        gidroL3 = gidroLinks.Add(gidroP4, gidroP5, gidro)
        gidroL4 = gidroLinks.Add(gidroP5, gidroP6, gidro)

        outputPoint.Offset = foundatP2.Offset
        outputPoint.Elevation = foundatP2.Elevation
        '-----------------------
        'создание песка
        '-----------------------
        'объявляем коллекции точек, связей и форм
        Dim soilPoints As PointCollection = corridorState.Points
        Dim soilLinks As LinkCollection = corridorState.Links

        Dim soilP1 As Point
        Dim soilP2 As Point
        Dim soilP3 As Point
        Dim soilP4 As Point

        Dim soilL1 As Link
        Dim soilL2 As Link
        Dim soilL3 As Link
        Dim soilL4 As Link

        Dim soilS As Shape

        soilP1 = soilPoints.Add(foundatP4.Offset, foundatP4.Elevation, "")
        soilP2 = soilPoints.Add(prepP10.Offset, prepP10.Elevation, "")
        If hasTargetOffset Then
            soilP3 = soilPoints.Add(soilWidth, soilP2.Elevation, "")
            soilP4 = soilPoints.Add(soilWidth, soilP1.Elevation, "")
        Else
            soilP3 = soilPoints.Add(insertPoint.Offset + (toothL1 + baseL1) * flip + soilWidth, soilP2.Elevation, "")
            soilP4 = soilPoints.Add(insertPoint.Offset + (toothL1 + baseL1) * flip + soilWidth, soilP1.Elevation, "")
        End If

        soilL1 = soilLinks.Add(soilP1, soilP2, soilName)
        soilL2 = soilLinks.Add(soilP2, soilP3, "")
        soilL3 = soilLinks.Add(soilP3, soilP4, soilName)
        soilL4 = soilLinks.Add(soilP4, soilP1, soilName)


        soilS = Shapes.Add(soilL1, soilL2, soilL3, soilL4, soilName)
        '-----------------------
        'и геотекстиля в основании
        '-----------------------
        If hasGeotextileLower Then
            Dim gtxtPoints As PointCollection = corridorState.Points
            Dim gtxtLinks As LinkCollection = corridorState.Links

            Dim gtxtP1 As Point
            Dim gtxtP2 As Point
            Dim gtxtP3 As Point
            Dim gtxtP4 As Point
            Dim gtxtP5 As Point
            Dim gtxtP6 As Point

            Dim gtxtL1 As Link
            Dim gtxtL2 As Link
            Dim gtxtL3 As Link
            Dim gtxtL4 As Link
            Dim gtxtL5 As Link

            gtxtP1 = gtxtPoints.Add(soilP4.Offset, soilP4.Elevation, "")
            gtxtP2 = gtxtPoints.Add(soilP1.Offset - 0.001 * flip, soilP1.Elevation, "")
            gtxtP3 = gtxtPoints.Add(soilP2.Offset, soilP2.Elevation, "")
            gtxtP4 = gtxtPoints.Add(gidroP6.Offset, gidroP6.Elevation, "")
            gtxtP5 = gtxtPoints.Add(gtxtP4.Offset + 0.001 * flip, gtxtP4.Elevation + baseLayerHeight, "")
            gtxtP6 = gtxtPoints.Add(gtxtP5.Offset + flip * geotxtOverlap, gtxtP5.Elevation, "")

            gtxtL1 = gtxtLinks.Add(gtxtP1, gtxtP2, geotextileName)
            gtxtL2 = gtxtLinks.Add(gtxtP2, gtxtP3, geotextileName)
            gtxtL3 = gtxtLinks.Add(gtxtP3, gtxtP4, geotextileName)
            gtxtL4 = gtxtLinks.Add(gtxtP4, gtxtP5, geotextileName)
            gtxtL5 = gtxtLinks.Add(gtxtP5, gtxtP6, geotextileName)
        End If
        'Создание полиэтиленовой пленки
        Dim fP1 As Point = foundPoints.Add(foundatP3.Offset - flip * 0.1, foundatP3.Elevation, "")
        Dim fP2 As Point = foundPoints.Add(foundatP4.Offset + flip * 0.1, foundatP4.Elevation, "")
        Dim fL1 As Link = foundLinks.Add(fP1, fP2, plenka)
    End Sub
    'метод для создания щебеночной подушки
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
    Sub CreateBaseWithSteps(corridorState As CorridorState,
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
                    beforeRegName As String,
                    afterRegName As String
                    )

        Dim maxH = subHeight + fStep
        Dim l2 = fStep / slope
        Dim currRegStart = corridorState.CurrentRegionStartStation
        Dim currRegEnd = corridorState.CurrentRegionEndStation
        Dim currStat = corridorState.CurrentStation
        Dim gridName As String = "Триакс"
        Dim solidName As String = "Щебень основания"
        Dim pitName As String = "Котлован"
        Dim slopeName As String = "Откосы"
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
                    beforeRegName)
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
                        beforeRegName)
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
                            afterRegName)
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
                        afterRegName)
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
                        slopeName)
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
                        slopeName As String)
        '---------------------------------------------------------
        'Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim gravPoints As PointCollection
        gravPoints = corridorState.Points
        Dim gravLinks As LinkCollection
        gravLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim gridPoints As PointCollection
        gridPoints = corridorState.Points
        Dim gridLinks As LinkCollection
        gridLinks = corridorState.Links
        '------------------------------------
        Dim gravP1 As Point
        Dim gravP2 As Point
        Dim gravP3 As Point
        Dim gravP4 As Point
        Dim gravP5 As Point
        Dim gravP6 As Point
        Dim gravLink1 As Link
        Dim gravLink2 As Link
        Dim gravLink3 As Link
        Dim gravLink4 As Link

        Dim gridP1 As Point
        Dim gridP2 As Point
        Dim gridLink As Link

        Dim gravShape As Autodesk.Civil.DatabaseServices.Shape
        gravP1 = gravPoints.Add(0, 0, "")
        gravP2 = gravPoints.Add(gravP1.Offset - flip * oSOffset, gravP1.Elevation - tHeight, pitName)
        gravP3 = gravPoints.Add(gravP1.Offset + flip * oWidth, gravP1.Elevation, "")
        gravP4 = gravPoints.Add(gravP3.Offset + flip * oSOffset, gravP3.Elevation - tHeight, pitName)
        gravLink1 = gravLinks.Add(gravP2, gravP4, pitName)

        Dim regionParam = corridorState.ParamsString("Имя участка")
        Dim regName = regionParam.Value
        Dim i As Integer = 1
        Do While i <= oGCount
            gravP5 = gravPoints.Add(gravP2.Offset - flip * oHeight / slope, gravP2.Elevation + oHeight, "")
            gravP6 = gravPoints.Add(gravP4.Offset + flip * oHeight / slope, gravP4.Elevation + oHeight, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

            gridP1 = gravPoints.Add(gravP2.Offset, gravP2.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "1")
            gridP2 = gravPoints.Add(gravP4.Offset, gravP4.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "2")
            gridLink = gridLinks.Add(gridP1, gridP2, regName + "_" + CStr(i) + "_" + gridName)

            gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, regName + "_" + CStr(i) + "_" + solidName)

            gravP2 = gravP5
            gravP4 = gravP6

            i += 1
        Loop
    End Sub
    Sub MaxSubBase(corridorState As CorridorState,
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
                        otherRegName As String)
        '---------------------------------------------------------
        'Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim gravPoints As PointCollection
        gravPoints = corridorState.Points
        Dim gravLinks As LinkCollection
        gravLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim gridPoints As PointCollection
        gridPoints = corridorState.Points
        Dim gridLinks As LinkCollection
        gridLinks = corridorState.Links
        '------------------------------------
        Dim gravP1 As Point
        Dim gravP2 As Point
        Dim gravP3 As Point
        Dim gravP4 As Point
        Dim gravP5 As Point
        Dim gravP6 As Point

        Dim gravLink1 As Link
        Dim gravLink2 As Link
        Dim gravLink3 As Link
        Dim gravLink4 As Link

        Dim gridP1 As Point
        Dim gridP2 As Point
        Dim gridLink As Link

        Dim gravShape As Autodesk.Civil.DatabaseServices.Shape

        Dim regionParam = corridorState.ParamsString("Имя участка")
        Dim regName = regionParam.Value
        Dim stepToLower = oHeight - (oGCount * oHeight - oFStep)

        gravP1 = gravPoints.Add(0, 0, "")
        gravP2 = gravPoints.Add(gravP1.Offset - flip * oSOffset, gravP1.Elevation - tHeight, pitName)
        gravP3 = gravPoints.Add(gravP1.Offset + flip * oWidth, gravP1.Elevation, "")
        gravP4 = gravPoints.Add(gravP3.Offset + flip * oSOffset, gravP3.Elevation - tHeight, pitName)
        gravLink1 = gravLinks.Add(gravP2, gravP4, pitName)

        'gridP1 = gravPoints.Add(gravP1.Offset - flip * oSOffset, gravP1.Elevation - tHeight, "1_" + gridName + "_1")
        'gridP2 = gravPoints.Add(gravP3.Offset + flip * oSOffset, gravP3.Elevation - tHeight, "2_" + gridName + "_1")
        'gridLink = gravLinks.Add(gridP1, gridP2, gridName + "_1")

        Dim i As Integer = 1
        Do While i <= oGCount - 1 'нижний матрас без верхнего слоя
            gravP5 = gravPoints.Add(gravP2.Offset - flip * oHeight / slope, gravP2.Elevation + oHeight, "")
            gravP6 = gravPoints.Add(gravP4.Offset + flip * oHeight / slope, gravP4.Elevation + oHeight, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

            gridP1 = gravPoints.Add(gravP2.Offset, gravP2.Elevation, otherRegName + "_" + CStr(i) + "_" + gridName + "_" + "1")
            gridP2 = gravPoints.Add(gravP4.Offset, gravP4.Elevation, otherRegName + "_" + CStr(i) + "_" + gridName + "_" + "2")
            gridLink = gridLinks.Add(gridP1, gridP2, otherRegName + "_" + CStr(i) + "_" + gridName)

            gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + CStr(i) + "_" + solidName)

            gravP2 = gravP5
            gravP4 = gravP6

            i += 1
        Loop
        'верхний слой нижнего матраса
        gravP5 = gravPoints.Add(gravP2.Offset - flip * stepToLower / slope, gravP2.Elevation + stepToLower, "")
        gravP6 = gravPoints.Add(gravP4.Offset + flip * stepToLower / slope, gravP4.Elevation + stepToLower, "")
        gravLink1 = gravLinks.Add(gravP2, gravP4, "")
        gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
        gravLink3 = gravLinks.Add(gravP6, gravP5, "")
        gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

        gridP1 = gravPoints.Add(gravP2.Offset, gravP2.Elevation, otherRegName + "_" + CStr(i) + "_" + gridName + "_" + "1")
        gridP2 = gravPoints.Add(gravP4.Offset, gravP4.Elevation, otherRegName + "_" + CStr(i) + "_" + gridName + "_" + "2")
        gridLink = gridLinks.Add(gridP1, gridP2, otherRegName + "_" + CStr(i) + "_" + gridName)

        gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + CStr(i) + "_" + solidName)
        gravP2 = gravP5
        gravP4 = gravP6

        i = 1
        Do While i <= oGCount  'верхний матрас целиком
            gravP5 = gravPoints.Add(gravP2.Offset - flip * oHeight / slope, gravP2.Elevation + oHeight, "")
            gravP6 = gravPoints.Add(gravP4.Offset + flip * oHeight / slope, gravP4.Elevation + oHeight, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

            gridP1 = gravPoints.Add(gravP2.Offset, gravP2.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "1")
            gridP2 = gravPoints.Add(gravP4.Offset, gravP4.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "2")
            gridLink = gridLinks.Add(gridP1, gridP2, regName + "_" + CStr(i) + "_" + gridName)

            gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, regName + "_" + CStr(i) + "_" + solidName)

            gravP2 = gravP5
            gravP4 = gravP6
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
                        otherRegName As String)
        '---------------------------------------------------------
        'Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim gravPoints As PointCollection
        gravPoints = corridorState.Points
        Dim gravLinks As LinkCollection
        gravLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim gridPoints As PointCollection
        gridPoints = corridorState.Points
        Dim gridLinks As LinkCollection
        gridLinks = corridorState.Links
        '------------------------------------
        Dim gravP1 As Point
        Dim gravP2 As Point
        Dim gravP3 As Point
        Dim gravP4 As Point
        Dim gravP5 As Point
        Dim gravP6 As Point
        Dim gravP7 As Point
        Dim gravP8 As Point

        Dim gravLink1 As Link
        Dim gravLink2 As Link
        Dim gravLink3 As Link
        Dim gravLink4 As Link

        Dim gridP1 As Point
        Dim gridP2 As Point
        Dim gridLink As Link

        Dim gravShape As Autodesk.Civil.DatabaseServices.Shape

        Dim regionParam = corridorState.ParamsString("Имя участка")
        Dim regName = regionParam.Value
        Dim i As Integer = 1
        Dim stepToLower = oFStep - (oGCount - 1) * oHeight
        Dim dH = tHeight - oGCount * oHeight
        gravP1 = gravPoints.Add(0, 0, "")
        gravP2 = gravPoints.Add(gravP1.Offset - flip * oSOffset, gravP1.Elevation - tHeight, pitName)
        gravP3 = gravPoints.Add(gravP1.Offset + flip * oWidth, gravP1.Elevation, "")
        gravP4 = gravPoints.Add(gravP3.Offset + flip * oSOffset, gravP3.Elevation - tHeight, pitName)
        gravLink1 = gravLinks.Add(gravP2, gravP4, pitName)
        If dH < stepToLower Then
            'если сечение в пределах щебеночной прослойки между матрасами
            gravP5 = gravPoints.Add(gravP2.Offset - flip * dH / slope, gravP2.Elevation + dH, "")
            gravP6 = gravPoints.Add(gravP4.Offset + flip * dH / slope, gravP4.Elevation + dH, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

            gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + solidName)

            Do While i <= oGCount
                gravP7 = gravPoints.Add(gravP5.Offset - flip * oHeight / slope, gravP5.Elevation + oHeight, "")
                gravP8 = gravPoints.Add(gravP6.Offset + flip * oHeight / slope, gravP6.Elevation + oHeight, "")

                gravLink1 = gravLinks.Add(gravP5, gravP6, "")
                gravLink2 = gravLinks.Add(gravP6, gravP8, slopeName + "2")
                gravLink3 = gravLinks.Add(gravP8, gravP7, "")
                gravLink4 = gravLinks.Add(gravP7, gravP5, slopeName + "1")

                gridP1 = gravPoints.Add(gravP5.Offset, gravP5.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "1")
                gridP2 = gravPoints.Add(gravP6.Offset, gravP6.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "2")
                gridLink = gridLinks.Add(gridP1, gridP2, regName + "_" + CStr(i) + "_" + gridName)

                gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, regName + "_" + CStr(i) + "_" + solidName)

                gravP5 = gravP7
                gravP6 = gravP8
                i += 1
            Loop

            'gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            'gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            'gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            'gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")
            '
            'gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, solidName)

        Else 'если сечение глубже щебеночной прослойки
            'оцениваем глубину
            Dim lower = dH - stepToLower
            Dim layers As Integer = 0
            Try
                layers = lower \ oHeight
            Catch
            End Try
            Dim remLayer = lower Mod oHeight

            'нижняя часть добавки щебеночной призмы нижнего слоя
            gravP5 = gravPoints.Add(gravP2.Offset - flip * remLayer / slope, gravP2.Elevation + remLayer, "")
            gravP6 = gravPoints.Add(gravP4.Offset + flip * remLayer / slope, gravP4.Elevation + remLayer, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP6, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            gravLink4 = gravLinks.Add(gravP5, gravP2, slopeName + "1")

            'gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + solidName)

            'создаем необходимое количество слоев нижнего матраса
            i = 1
            Do While i <= layers

                Dim layerCount As Integer = oGCount - layers + i

                gravP7 = gravPoints.Add(gravP5.Offset - flip * oHeight / slope, gravP5.Elevation + oHeight, "")
                gravP8 = gravPoints.Add(gravP6.Offset + flip * oHeight / slope, gravP6.Elevation + oHeight, "")

                gravLink1 = gravLinks.Add(gravP5, gravP6, "")
                gravLink2 = gravLinks.Add(gravP6, gravP8, slopeName + "2")
                gravLink3 = gravLinks.Add(gravP8, gravP7, "")
                gravLink4 = gravLinks.Add(gravP7, gravP5, slopeName + "1")

                gridP1 = gravPoints.Add(gravP5.Offset, gravP5.Elevation, otherRegName + "_" + CStr(layerCount) + "_" + gridName + "_" + "1")
                gridP2 = gravPoints.Add(gravP6.Offset, gravP6.Elevation, otherRegName + "_" + CStr(layerCount) + "_" + gridName + "_" + "2")
                gridLink = gridLinks.Add(gridP1, gridP2, otherRegName + "_" + CStr(layerCount) + "_" + gridName)

                'gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + solidName + "_" + CStr(i))

                gravP5 = gravP7
                gravP6 = gravP8
                i += 1
            Loop

            gravP7 = gravPoints.Add(gravP5.Offset - flip * stepToLower / slope, gravP5.Elevation + stepToLower, "")
            gravP8 = gravPoints.Add(gravP6.Offset + flip * stepToLower / slope, gravP6.Elevation + stepToLower, "")

            gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            gravLink2 = gravLinks.Add(gravP4, gravP8, slopeName + "2")
            gravLink3 = gravLinks.Add(gravP8, gravP7, "")
            gravLink4 = gravLinks.Add(gravP7, gravP2, slopeName + "1")

            gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, otherRegName + "_" + solidName)

            gravP5 = gravP7
            gravP6 = gravP8
            ' создаем верхние слои матраса
            i = 1
            Do While i <= oGCount
                gravP7 = gravPoints.Add(gravP5.Offset - flip * oHeight / slope, gravP5.Elevation + oHeight, "")
                gravP8 = gravPoints.Add(gravP6.Offset + flip * oHeight / slope, gravP6.Elevation + oHeight, "")

                gravLink1 = gravLinks.Add(gravP5, gravP6, "")
                gravLink2 = gravLinks.Add(gravP6, gravP8, slopeName + "2")
                gravLink3 = gravLinks.Add(gravP8, gravP7, "")
                gravLink4 = gravLinks.Add(gravP7, gravP5, slopeName + "1")

                gridP1 = gravPoints.Add(gravP5.Offset, gravP5.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "1")
                gridP2 = gravPoints.Add(gravP6.Offset, gravP6.Elevation, regName + "_" + CStr(i) + "_" + gridName + "_" + "2")
                gridLink = gridLinks.Add(gridP1, gridP2, regName + "_" + CStr(i) + "_" + gridName)

                gravShape = Shapes.Add(gravLink1, gravLink2, gravLink3, gravLink4, regName + "_" + CStr(i) + "_" + solidName)

                gravP5 = gravP7
                gravP6 = gravP8

                i += 1
            Loop

            'gravLink1 = gravLinks.Add(gravP2, gravP4, "")
            'gravLink2 = gravLinks.Add(gravP4, gravP6, "")
            'gravLink3 = gravLinks.Add(gravP6, gravP5, "")
            'gravLink4 = gravLinks.Add(gravP5, gravP2, "")


        End If
    End Sub
    Sub SubbaseAddStations(tm As DBTransactionManager,
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

#End Region
End Class


