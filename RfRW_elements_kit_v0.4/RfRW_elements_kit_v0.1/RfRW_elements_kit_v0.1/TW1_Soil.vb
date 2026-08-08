Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Imports Shape = Autodesk.Civil.DatabaseServices.Shape
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode
Imports System.IO



Public Class TW1_Soil
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: BasicLane
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
    '                FaceAngle              double      no          0                degrees of face element slope
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
    Private Const dFaceAngleDefault = 4
    Private Const dHorizOffset = 0.0
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const dLayerStepDefault = 0.45
    Private Const dGravelSlopeDefault = 1.5
    Private Const dDrenageOffsetDefault = 0.3
    Private Const dDrenageElevationDefault = 1
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dBaseElevationDefault = 0.15
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    Private Const dSubAsNameDefault = "Участок"
    'Private Const blocksAboveGrid As Integer = 0
    'Private Const dBlocksCount As Integer = 1
    'Private Const dBlockH = 0.5
    Private Const dBlockWidth = 0.214
    Private Const dBlocksInLayout = 5
    Private Const dBlockHeight = 0.15
    Private Const deltaH = 0.000
    Private Const dBlockOffset = 0.010459
    Private Const dBlockLength = 0.4
    Private Const dPipeSlope = 0.05
    Private Const dPipeStep = 5.0
    Private Const dPipeDiametr = 0.16


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

        ParamLong = paramsLong.Add("Проектный профиль", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"

        ParamLong = paramsLong.Add("blocksTop", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных блоков"

        ParamLong = paramsLong.Add("BackProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль тыльной стороны"
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
        paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        paramsDouble.Add("BlocksDeltaH", deltaH)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
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
        'Dim oHorizStep As Double
        'Try
        '    oHorizStep = paramsDouble.Value("Горизонтальное смещение")
        'Catch
        '    oHorizStep = dHorizOffset
        'End Try
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
        Dim gOverlap As Double
        Try
            gOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            gOverlap = dGeotextileOverlapDefault
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
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
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

#End Region
        ' Check user input
        If oGridWidth < 2 Then
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
            '--------------------------------------------------------
            'анализируем наличие целей для сбора информации в сечении
            '--------------------------------------------------------
            'Dim oBlocks = paramsLong.Value("bCount")
            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Проектный профиль")
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
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If
            'On Error GoTo ErrorHandler
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

            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / blockStep, Integer)
                createAddStationsForProfile(tm, corridorState, blocksElevTarget)

            Else 'в случае отсутствия профиля для определения высоты облицовки (для первого прохода например)
                'в начале каждого региона(области) добавляем сечения в пикетах шага облицовочного блока TW1
                If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, blockStep, dL, elevationTarget) 'доп сечения для облицовки
                    createPipeAddStations(tm, corridorState, oPipeStep, oPipeSlope) 'доп сечения для трубы
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = blockStep * 1000
                    _blocksCount = dWallHeightElevation * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 2
                        If isStep(tm, corridorState, firstTop) Then
                                _blocksCount -= 1
                            Else
                                Throw New Exception("что-то неладное")
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
            'paramsLong.Item("BlocksCount").Value = rows
            Dim lowerLayerH As Double = oBaseElevation
            Dim levelWidth = 0.21 'ширина выравнивающего слоя по облицовке

                '--------------------------------
                'построение конструкции в сечении
                '--------------------------------
                Dim pointToSoil As New PointInMem
                Dim zeroPoint As New PointInMem With {
                .Offset = 0,
                .Elevation = 0
                }
            'создание облицовочных блоков
            createFacingTW(corridorState, dWallHeightElevation, flip, rows, blockStep, dH, levelWidth, zeroPoint, pointToSoil)
            pointToSoil.Offset += flip * (dBlockWidth / 2 - 0.01) 'т.к. точка вставки = середине нижнего блока, смещаем ее на пол блока за исключением фаски
                'создание армогрунта
                wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation,
                       oGridWidth, gStep, dHorizOffset,
                       lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope,
                       geotextileOverlap, dFaceAngleDefault, flip, oSubAsName,
                       RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                       rows, blockStep,
                       hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil)
            Else 'для представления шаблона конструкции
                '----------------------------------
                'строим шаблон конструкции
                '----------------------------------
                Dim levelWidth = 0.21
                Dim levelH = 0.1
                'Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH
                Dim hasTarget As Boolean = False
            Dim dWallHeightElevation As Double = oBaseElevation + (RE520_count + RE540_count + RE560_count + RE570_count + RE580_count) * gStep + levelH
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False
            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double
            Dim blockLayers As Integer = dWallHeightElevation * 1000 \ blockStep * 1000
            Dim lowerLayerH As Double = oBaseElevation
            'точка для вставки армогрунта
            Dim pointToSoil As New PointInMem

            'создание облицовочных блоков
            createFacingTW(corridorState, dWallHeightElevation, flip, blockLayers, blockStep, dH, levelWidth, oOrigin, pointToSoil)
            pointToSoil.Offset += flip * (dBlockWidth / 2 - 0.01) 'т.к. точка вставки = середине нижнего блока, смещаем ее на пол блока за исключением фаски
                wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation,
oGridWidth, gStep, dHorizOffset,
lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope,
geotextileOverlap, dFaceAngleDefault, flip, oSubAsName,
RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
blockLayers, blockStep,
hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil)
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
            param = paramsDouble.Add("Перехлест геотекстиля", gOverlap)
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
                           ByVal horizontalStep As Double,
                           ByVal baseLayerStep As Double,
                           ByVal drenageElevLayer As Long,
                           ByVal drenageWidth As Double,
                           ByVal drenageSlope As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal faceAngle As Double,
                           ByVal flipValue As Double,
                           ByVal subAsName As String,
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
                           ByVal startInputPoint As PointInMem
                           )
        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem
        Dim elevatP As Double = startInputPoint.Elevation 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = startInputPoint.Offset
        insertPoint.Offset = offsetP 'присваиваем значения опорной точке
        insertPoint.Elevation = elevatP

        Dim dX As Double = (horizontalStep + verticalStep * Math.Tan(faceAngle * Math.PI / 180)) * flipValue 'отступ для каждого вышележащего ряда (в метрах) 
        'определим кол-во слоев
        Dim layers As Integer
        layers = RE580Count + RE570Count + RE560Count + RE540Count + RE520Count
        'layers = wallHeight * 1000 \ verticalStep * 1000
        'определим остаток сверху
        'Dim reminder As Double
        'reminder = wallHeight Mod verticalStep
        Dim blocksInLayer As Double = verticalStep / blockHeight 'блоков в одном слое
        Dim blockCountForLayers As Integer = blocksCount - ((baseLayerStep * 1000) \ (blockHeight * 1000)) 'блоков выше первого слоя решетки
        Dim maxLayers As Integer = blockCountForLayers \ CType(blocksInLayer, Integer) 'максимальное ЦЕЛОЕ число слоев исходя из кол-ва облицовочных блоков 
        Dim blocksReminder As Integer = blockCountForLayers Mod CType(blocksInLayer, Integer) 'остаток облицовочных блоков выше ЦЕЛОГО числа слоев
        If layers > maxLayers Then 'условие не выше облицовки
            If blocksInLayer > 1 And blocksReminder = 0 Then 'доп условие для маленького блока
                layers = maxLayers - 1
                blocksReminder = blocksInLayer
            Else 'для большого блока или если у маленького есть хотя бы один блок выше последнего слоя
                layers = maxLayers
            End If
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
        'Dim linkName As String

        Dim i As Integer = 0

        'песок ниже первой георешетки
        createLowerLayer(corridorState, subAsName, soilName, wallWidth, baseLayerStep, faceAngle, flipValue, insertPoint, i, hasTargetOffset)
        elevatP += baseLayerStep
        offsetP += baseLayerStep * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        i += 1
        'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
        Do While layers >= i
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
                createNonDrenageLayer(corridorState, subAsName, soilName, wallWidth, verticalStep, faceAngle, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, i, hasTargetOffset, isLast, isDrenLayers)
            Else 'If drenageElevLayer < i And i < layers Then 'слои с дренажной призмой
                createDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                isFirst = False
            End If
            gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
            elevatP += verticalStep
            offsetP += dX
            i += 1
        Loop
        insertPoint.Offset = offsetP
        insertPoint.Elevation = elevatP
        'добавляем еще один слой решетки
        gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
        'проводим анализ оставшегося пространства
        Dim reminder As Double = wallHeight - elevatP 'остаток
        If i <= drenageElevLayer Then
            createLastNonDrenageLayer(corridorState, soilName, wallWidth, reminder, wallBackHeight, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, hasTargetElevation)
        Else
            If blocksReminder < 3 And reminder <= verticalStep Then
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            ElseIf blocksReminder < 3 And reminder > verticalStep Then
                createDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                reminder -= verticalStep
                elevatP += verticalStep
                offsetP += dX
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            ElseIf blocksReminder >= 3 Then
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, False, isFirst, blockHeight)
                reminder -= blockHeight
                elevatP += blockHeight
                offsetP += blockHeight * Math.Tan(faceAngle * Math.PI / 180) * flipValue
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                i += 1
                'добавляем еще один слой решетки
                gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            Else
                Throw New Exception("какая-то лажа с верхними слоями армогрунта")
            End If
        End If
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

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, linkName + "1")
        gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth * flipValue, pointToInsert.Elevation, linkName + "2")
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
                                 ByVal subAsName As String,
                                 ByVal soilName As String,
                                 ByVal soilWidth As Double,
                                 ByVal layerHeight As Double,
                                 ByVal faceAngle As Double,
                                 ByVal flipValue As Double,
                                 ByVal pointToInsert As PointInMem,
                                 ByVal layerCounter As Integer,
                                 ByVal hasTargetOffset As Boolean
                                 )
        '----------------------------
        'sand before first grid
        '----------------------------
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fSOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'firstStepOffset
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
        sandPoint2 = sandPoints.Add(sandPoint1.Offset + fSOffset, sandPoint1.Elevation + layerHeight, sandPointName2)
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
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "Низ дренирующего грунта")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, soilName)
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
    End Sub
    'создание слоя засыпки дренирующим грунтом
    Private Sub createNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal subAsName As String,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal faceAngle As Double,
                                      ByVal drenageWidth As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal layerCounter As Integer,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal isLastlayer As Boolean,
                                      ByVal isAnyDrenageLayers As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'вычисляем вспомогательные параметры
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim SOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'layerStepOffset
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
        sandPoint2 = sandPoints.Add(sandPoint1.Offset + SOffset, sandPoint1.Elevation + layerHeight, sandPointName2)
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
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
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
                                   ByVal subAsName As String,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double,
                                   ByVal faceAngle As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal layerCounter As Integer,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal pipeStep As Double,
                                   ByVal pipeSlope As Double,
                                   ByVal pipeDiameter As Double
                                   )
        'вычисляем вспомогательные параметры
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fOffset As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
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
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset + fOffset, drPoint1.Elevation + layerHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
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
        Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
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
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset + dOffset - fOffset, sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)

        Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, gtxtName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, gtxtName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, gtxtName)

        If isFirstlayer Then 'создание дренажной трубы и геомембраны в основании пристеночного дренажа
            Dim dPoint As Point
            createPipeAxis(corridorState, pipeStep, pipeSlope, pointToInsert, dPoint, pipeDiameter, flipValue)
            createPipe(corridorState, dPoint, pipeDiameter)
            createGeomembrane(corridorState, pointToInsert, pipeDiameter, drenageWidth, layerHeight, drenageSlope, dFaceAngleDefault, flipValue)
        End If
    End Sub
    'создание слоя засыпки с пристеночным дренажом самого верхнего слоя (с возможностью задать перехлест геотекстиля с нижне лежащим слоем) 
    Private Sub createLastDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal subAsName As String,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal gtxtName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double, 'высота стандартного слоя
                                   ByVal layerHeightBack As Double,
                                   ByVal faceAngle As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal layerCounter As Integer,
                                   ByVal hasTargetOffset As Boolean,
                                   ByVal hasTargetElev As Boolean,
                                   ByVal isFirstlayer As Boolean,
                                   ByVal lastHeight As Double 'высота последнего слоя
                                   )
        'вычисляем вспомогательные параметры
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fOffset As Double = lastHeight * Math.Tan(faceSlope) * flipValue 'last layer FaceOffset
        Dim dOffset As Double = lastHeight * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        Dim gtxtLow As Double = (layerHeight * drenageSlope) * flipValue
        Dim gtxtTop As Double = (drenageWidth + dOffset) * flipValue
        Dim fOffsetLow As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "" ' subAsName & "_" & CStr(layerCounter) & "_" & "gravel" & 4
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset + fOffset, drPoint1.Elevation + lastHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint1.Offset + gOffset + dOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(pointToInsert.Offset + gOffset, pointToInsert.Elevation, drPointName4)
        'declare description for links
        'Dim drLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "gravelUp"
        'Dim drLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drenageName)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, drenageName)
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
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
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
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, soilName)
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
        Dim geotextilePoint5 As Point
        Dim geotextilePoint6 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link
        Dim geotextileLink4 As Link

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
            geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + (gtxtOffset + gtxtLow) - fOffsetLow, sandPoint1.Elevation, geotextilePointName1)
        End If
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        'geotextilePoint5 = geotxtPoints.Add(geotextilePoint4.Offset, geotextilePoint4.Elevation, geotextilePointName5)
        ' geotextilePoint6 = geotxtPoints.Add(geotextilePoint5.Offset - (gtxtOffset + gtxtTop), geotextilePoint5.Elevation, geotextilePointName6)


        Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"
        Dim geotextileLinkName3 As String = subAsName & "_" & "geotextileTop"

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
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
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
            sandPoint3.Elevation = layerHeightBack
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, soilName)
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
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
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
    Private Sub createGeomembrane(corridorState As CorridorState, ByVal insertPoint As PointInMem, pDiam As Double, drenageOffset As Double, layerHeight As Double, layerSlope As Double, faceAngle As Double, flip As Double)

        Dim membranePoints As PointCollection
        membranePoints = corridorState.Points
        Dim membraneLinks As LinkCollection
        membraneLinks = corridorState.Links

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
        Dim faceSlope = faceAngle * Math.PI / 180
        Dim faceTan = Tan(faceSlope) * flip

        P1 = membranePoints.Add(insertPoint.Offset + membraneHeightFace * faceTan, insertPoint.Elevation + membraneHeightFace, "")
        P2 = membranePoints.Add(insertPoint.Offset, insertPoint.Elevation, "")
        P3 = membranePoints.Add(P2.Offset + drenageOffset * flip, P2.Elevation, "")
        P4 = membranePoints.Add(P3.Offset + layerHeight * layerSlope * flip, P3.Elevation + layerHeight, "")

        Dim membraneName As String = "Геомембрана"

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
#End Region
#Region "Создание облицовки"
    'создание конструкции
    Public Sub createFacingTW(ByVal corridorState As CorridorState, ByVal dWallHeight As Double, ByVal flipValue As Double, ByVal blockRows As Integer, ByVal blockVerticalStep As Double, ByVal delHeight As Double, ByVal levelingWidth As Double, ByVal origin As PointInMem, ByRef outputPoint As PointInMem)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        'создание облицовочных блоков
        Dim totalOffset = dWallHeight * dBlockOffset / blockVerticalStep
        Dim totalHeight = blockRows * blockVerticalStep

        'точки вставки облицовочных блоков
        Dim newAddPoint As New PointInMem With {
        .Offset = origin.Offset - totalOffset * flipValue,
        .Elevation = origin.Elevation
        }
        'точка вставки омоноличивания
        Dim levelingTopPoint As New PointInMem With {
        .Elevation = origin.Elevation + dWallHeight,
        .Offset = 0
        }
        'точка вывода середины первого блока
        outputPoint.Offset = newAddPoint.Offset + flipValue * dBlockWidth / 2
        outputPoint.Elevation = newAddPoint.Elevation
        Dim i As Integer = 1
        createLowerBlock(corridorState, newAddPoint, i, flipValue)
        i += 1
        Dim topPntAdd As New PointInMem With {
             .Offset = origin.Offset,
            .Elevation = origin.Elevation + blockRows * (dBlockHeight + delHeight)
            }
        levelingTop(corridorState, levelingTopPoint, topPntAdd, levelingWidth, flipValue)
        While i <= blockRows
            newAddPoint.Offset += dBlockOffset * flipValue
            newAddPoint.Elevation += blockVerticalStep
            createBlock(corridorState, newAddPoint, i, flipValue)
            i += 1
        End While
        'newAddPoint.Offset += dBlockOffset * flipValue
        'newAddPoint.Elevation += blockVerticalStep

        'Dim oParam As IParam
        'oParam = paramsLong.Add("BlocksCount", blockRows)
        'If oParam IsNot Nothing Then
        '    oParam.Access = ParamAccessType.Output
        'End If
        'oParam = paramsDouble.Add("BlockHeight", dBlockHeight)
        'If oParam IsNot Nothing Then
        '    oParam.Access = ParamAccessType.Output
        'End If
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

        P16 = blockPoints.Add(P15.Offset - 0.009 * flip, P1.Elevation, "ось раскладки блоков")

        Dim blockName As String = "TW1"
        Dim blockFace As String = "Лицевая грань блока"
        Dim nameFace As String() = {blockName, blockFace}

        L1 = blockLinks.Add(P1, P2, nameFace)
        L2 = blockLinks.Add(P2, P3, nameFace)
        L3 = blockLinks.Add(P3, P4, nameFace)
        L4 = blockLinks.Add(P4, P5, blockName)
        L5 = blockLinks.Add(P5, P6, blockName)
        L6 = blockLinks.Add(P6, P7, blockName)
        L7 = blockLinks.Add(P7, P8, blockName)
        L8 = blockLinks.Add(P8, P9, blockName)
        L9 = blockLinks.Add(P9, P10, blockName)
        L10 = blockLinks.Add(P10, P11, blockName)
        L11 = blockLinks.Add(P11, P12, blockName)
        L12 = blockLinks.Add(P12, P13, blockName)
        L13 = blockLinks.Add(P13, P14, blockName)
        L14 = blockLinks.Add(P14, P15, blockName)
        L15 = blockLinks.Add(P15, P16, blockName)
        L16 = blockLinks.Add(P16, P1, blockName)

        'Dim blockName As String = CType(rowNum, String) + "_" + "TW1"

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

        P13 = blockPoints.Add(addPoint.Offset + (dBlockWidth) * flip / 2, addPoint.Elevation, "Ось первого ряда блоков")

        Dim blockName As String = "TW1"
        Dim blockFace As String = "Лицевая грань блока"
        Dim nameFace As String() = {blockName, blockFace}

        L1 = blockLinks.Add(P1, P2, nameFace)
        L2 = blockLinks.Add(P2, P3, nameFace)
        L3 = blockLinks.Add(P3, P4, nameFace)
        L4 = blockLinks.Add(P4, P5, blockName)
        L5 = blockLinks.Add(P5, P6, blockName)
        L6 = blockLinks.Add(P6, P7, blockName)
        L7 = blockLinks.Add(P7, P8, blockName)
        L8 = blockLinks.Add(P8, P9, blockName)
        L9 = blockLinks.Add(P9, P10, blockName)
        L10 = blockLinks.Add(P10, P11, blockName)
        L11 = blockLinks.Add(P11, P12, blockName)
        L12 = blockLinks.Add(P12, P1, blockName)


        Dim shapeLinks() = {L1, L2, L3, L4, L5, L6, L7, L8, L9, L10, L11, L12}
        Shape = blockShapes.Add(shapeLinks, blockName)

    End Sub
    'метод для добавления шага блока
    Public Sub createAddStations(tm As DBTransactionManager, corridorState As CorridorState, blockStep As Double, blockLength As Double, target As SlopeElevationTarget)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находим пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.0001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt + blockLength / 2
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
        Dim sliseStep = blockLength / 2
        Do While stationCurr < endSt
            Dim wallHeight = target.GetElevation(alignmentId, stationCurr) - origin.Elevation
            Dim remainder = wallHeight Mod blockStep
            If Math.Abs(remainder) < 0.0001 Then 'уточнение расстояния до вертикального скачка кратное длине половины облицовочного блока
                Dim rem1 = (stationCurr - startSt) Mod sliseStep
                Dim backSlice = stationCurr - rem1
                Dim rem2 = sliseStep - rem1
                Dim frontSlice = stationCurr + rem2
                Dim backH = target.GetElevation(alignmentId, backSlice)
                Dim frontH = target.GetElevation(alignmentId, frontSlice)
                If frontH <= backH Then
                    sectionsToAdd.Add(backSlice)
                    sectionsToAddStep.Add(backSlice + 0.001)
                    stationCurr = backSlice + sliseStep + 0.001
                Else
                    sectionsToAdd.Add(frontSlice)
                    sectionsToAddStep.Add(frontSlice + 0.001)
                    stationCurr = frontSlice + 0.001
                End If
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
        Dim sectionsToAdd As New List(Of Double)
        Dim sectionsToAddStep As New List(Of Double)
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
End Class

